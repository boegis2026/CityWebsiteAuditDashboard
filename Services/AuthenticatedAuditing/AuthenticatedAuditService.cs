using System.Collections.Concurrent;
using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using System.Security.Cryptography;
using System.Text;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using System.Diagnostics;

namespace CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

/// <summary>
/// Manages live authenticated Playwright sessions and saves their audit
/// history to SQL Server.
///
/// This service will eventually be registered as a singleton because the same
/// browser must remain available across multiple dashboard requests.
/// </summary>
public sealed class AuthenticatedAuditService : IAuthenticatedAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuthenticatedAuditService> _logger;

    // Browser objects cannot be stored in SQL Server. They remain in memory
    // while the authenticated audit is running.
    private readonly ConcurrentDictionary<Guid, AuthenticatedAuditBrowserSession>
        _sessions = new();

    /*
    * Keep the proof-of-concept batch small enough to avoid accidentally
    * overwhelming a protected application or creating an excessively long
    * browser session.
    */
    private const int MaximumAuthenticatedBatchSize = 25;

    public AuthenticatedAuditService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuthenticatedAuditService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public AuthenticatedAuditSessionResult? GetActiveSession()
    {
        /*
         * Closing the visible Edge window may close all of its pages while the
         * underlying Playwright browser process remains connected.
         *
         * A usable session therefore requires both a connected browser and at
         * least one open HTTP or HTTPS page.
         */
        AuthenticatedAuditBrowserSession? session =
            _sessions.Values
                .Where(session =>
                    !session.IsStopping &&
                    session.Browser.IsConnected &&
                    session.BrowserContext.Pages.Any(IsAuditablePage))
                .OrderByDescending(session => session.StartedAt)
                .FirstOrDefault();

        if (session is null)
        {
            return null;
        }

        return new AuthenticatedAuditSessionResult
        {
            SessionId = session.SessionId,
            AuditRunId = session.AuditRunId,
            ApplicationName = session.ApplicationName,
            StartingUrl = session.StartingUrl,
            AccessibilityEngine = session.AccessibilityEngine,
            StartedAt = session.StartedAt
        };
    }

    public async Task<AuthenticatedAuditSessionResult> StartSessionAsync(
        AuthenticatedAuditStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string applicationName = request.ApplicationName.Trim();
        string startingUrl = request.StartingUrl.Trim();

        ValidateStartRequest(applicationName, startingUrl);

        /*
        * Only one browser session is supported for this local proof of concept.
        * Starting a second session could make it unclear which Edge window the
        * dashboard's Scan button controls.
        */
        if (GetActiveSession() is not null)
        {
            throw new InvalidOperationException(
                "An authenticated audit session is already running. " +
                "Return to the active session and complete it before starting another.");
        }

        DateTime startedAt = DateTime.UtcNow;
        Guid sessionId = Guid.NewGuid();

        // Create the database run before opening the browser so failures
        // during browser startup can still be recorded in the audit history.
        int auditRunId = await CreateAuditRunAsync(
            applicationName,
            startingUrl,
            startedAt,
            cancellationToken);

        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IBrowserContext? browserContext = null;

        try
        {
            playwright = await Playwright.CreateAsync();

            browser = await playwright.Chromium.LaunchAsync(
                new BrowserTypeLaunchOptions
                {
                /*
                * Use the workstation's installed Microsoft Edge instead of
                * Playwright's downloaded Chromium build.
                *
                * Managed workstations may restrict unfamiliar browser executables,
                * while the organization-managed Edge installation is already
                * approved and configured for the workstation.
                */
                Channel = "msedge", 

                // Authentication and workflow navigation are performed manually,
                // so the browser must remain visible.
                Headless = false
            });

            browserContext = await browser.NewContextAsync();

            IPage startingPage = await browserContext.NewPageAsync();

            await startingPage.GotoAsync(
                startingUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 120_000
                });

            var session = new AuthenticatedAuditBrowserSession
            {
                SessionId = sessionId,
                AuditRunId = auditRunId,
                ApplicationName = applicationName,
                StartingUrl = startingUrl,
                AccessibilityEngine = "axe-core",
                StartedAt = startedAt,
                Playwright = playwright,
                Browser = browser,
                BrowserContext = browserContext,
                ActivePage = startingPage
            };

            if (!_sessions.TryAdd(sessionId, session))
            {
                throw new InvalidOperationException(
                    "The authenticated browser session could not be registered.");
            }

            /*
            * The initial page existed before the BrowserContext.Page event handler was
            * registered, so attach its close handler directly.
            */
            RegisterPageCloseTracking(sessionId, startingPage);

            /*
             * The protected application may open in another tab after login. Register the
             * same close tracking for every additional page created in this context.
             */
            browserContext.Page += (_, newPage) =>
            {
                RegisterPageCloseTracking(sessionId, newPage);
            };

            /*
            * Detect when the user manually closes Edge or when the browser crashes.
            *
            * StopSessionAsync removes the session before intentionally closing the
            * browser, so this handler only owns unexpected browser disconnections.
            */
            browser.Disconnected += (_, _) =>
            {
                /*
                 * Event handlers cannot be awaited by Playwright. The helper catches and
                 * logs its own exceptions so failures are not silently lost.
                 */
                _ = HandleUnexpectedBrowserDisconnectAsync(sessionId);
            };

            /*
             * Cover the small possibility that Edge disconnected between registration
             * and attaching the event handler.
             */
            if (!browser.IsConnected)
            {
                _ = HandleUnexpectedBrowserDisconnectAsync(sessionId);
            }

            // Ownership of these objects now belongs to the in-memory session.
            // Clearing the local references prevents the catch block from
            // closing a successfully registered browser.
            playwright = null;
            browser = null;
            browserContext = null;

            _logger.LogInformation(
                "Started authenticated audit session {SessionId} for run {AuditRunId}.",
                sessionId,
                auditRunId);

            return new AuthenticatedAuditSessionResult
            {
                SessionId = sessionId,
                AuditRunId = auditRunId,
                ApplicationName = applicationName,
                StartingUrl = startingUrl,
                AccessibilityEngine = "axe-core",
                StartedAt = startedAt
            };
        }
        catch (Exception exception)
        {
            await ClosePartiallyCreatedBrowserAsync(
                browserContext,
                browser,
                playwright);

            string status = exception is OperationCanceledException
                ? "Cancelled"
                : "Failed";

            await MarkRunAsUnsuccessfulAsync(
                auditRunId,
                status,
                exception.Message);

            _logger.LogError(
                exception,
                "Authenticated audit run {AuditRunId} failed during browser startup.",
                auditRunId);

            throw;
        }
    }

    public AuthenticatedAuditProgressResult? GetProgress(
        Guid sessionId)
    {
        return _sessions.TryGetValue(
            sessionId,
            out AuthenticatedAuditBrowserSession? session)
                ? session.Progress
                : null;
    }

    public async Task<AuthenticatedAuditStepResult> ScanCurrentStepAsync(
    Guid sessionId,
    CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out AuthenticatedAuditBrowserSession? session))
        {
            throw new KeyNotFoundException(
                "The authenticated audit session was not found or is no longer running.");
        }

        if (!session.Browser.IsConnected)
        {
            throw new KeyNotFoundException(
                "The Playwright controlled Edge browser has been closed. " +
                "The authenticated audit session is no longer available.");
        }

        if (!session.BrowserContext.Pages.Any(IsAuditablePage))
        {
            throw new KeyNotFoundException(
                "All Playwright-controlled Edge pages have been closed. " +
                "The authenticated audit session is no longer available.");
        }

        // Only one scan or stop operation may use this browser session at a time.
        await session.OperationLock.WaitAsync(cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            SetProgress(
                session,
                isScanning: true,
                stage: "Opening page",
                stagePercent: 10,
                currentUrl: session.ActivePage?.Url,
                currentPageNumber: session.NextStepNumber,
                totalPageCount: null);

            /*
             * The session may have been removed while this request was waiting for
             * OperationLock. Never attempt to scan a browser that is shutting down.
             */
            if (session.IsStopping || !_sessions.ContainsKey(sessionId))
            {
                throw new KeyNotFoundException(
                    "The authenticated audit session is no longer running.");
            }

            int stepNumber = session.NextStepNumber;
            IPage? activePage = null;
            RenderedPageSnapshot? snapshot = null;

            string currentUrl =
                session.ActivePage is not null && !session.ActivePage.IsClosed
                    ? session.ActivePage.Url
                    : session.StartingUrl;

            AuthenticatedAuditStepResult stepResult;

            try
            {
                activePage = SelectPageForAudit(session);
                session.ActivePage = activePage;
                currentUrl = activePage.Url;

                // Bring the selected protected application tab forward so the user
                // can clearly see which page state is about to be scanned.
                await activePage.BringToFrontAsync();

                SetProgress(
                    session,
                    isScanning: true,
                    stage: "Waiting for rendering",
                    stagePercent: 25,
                    currentUrl: activePage.Url,
                    currentPageNumber: session.NextStepNumber,
                    totalPageCount: null);

                cancellationToken.ThrowIfCancellationRequested();

                await WaitForRenderedPageAsync(activePage);

                SetProgress(
                    session,
                        isScanning: true,
                        stage: "Running axe-core",
                        stagePercent: 50,
                        currentUrl: activePage.Url,
                        currentPageNumber: session.NextStepNumber,
                        totalPageCount: null);

                cancellationToken.ThrowIfCancellationRequested();

                snapshot = await CaptureRenderedPageSnapshotAsync(activePage);

                cancellationToken.ThrowIfCancellationRequested();

                // This is a read-only accessibility scan. The service does not click
                // Next, Submit, Finish, Pay, Certify, or any other workflow control.
                AxeResult axeResult = await activePage.RunAxe();

                SetProgress(
                    session,
                    isScanning: true,
                    stage: "Processing results",
                    stagePercent: 70,
                    currentUrl: activePage.Url,
                    currentPageNumber: session.NextStepNumber,
                    totalPageCount: null);

                cancellationToken.ThrowIfCancellationRequested();

                int violationRuleCount =
                    axeResult.Violations?.Count() ?? 0;

                int affectedElementCount =
                    axeResult.Violations?
                        .Sum(violation => violation.Nodes?.Count() ?? 0)
                    ?? 0;

                int needsReviewRuleCount =
                    axeResult.Incomplete?.Count() ?? 0;

                int passedRuleCount =
                    axeResult.Passes?.Count() ?? 0;

                /*
                 * Convert axe's detailed rule results into our own safe contract.
                 * This intentionally excludes node HTML, selectors, and entered values.
                 */
                List<AuthenticatedAuditFindingResult> findings =
                    CreateFindingResults(axeResult);

                string stepName = GetStepName(
                    snapshot,
                    stepNumber);

                stepResult = new AuthenticatedAuditStepResult
                {
                    StepNumber = stepNumber,
                    StepName = LimitLength(stepName, 200) ?? $"Step {stepNumber}",
                    Url = LimitLength(currentUrl, 2048) ?? session.StartingUrl,
                    PageTitle = LimitLength(snapshot.PageTitle, 500),
                    Heading = LimitLength(snapshot.Heading, 500),
                    DomFingerprint = CreateDomFingerprint(
                        snapshot.FingerprintSource),
                    ScannedAt = DateTime.UtcNow,
                    VisibleFormCount = snapshot.VisibleFormCount,
                    VisibleFieldCount = snapshot.VisibleFieldCount,
                    VisibleButtonCount = snapshot.VisibleButtonCount,
                    ViolationRuleCount = violationRuleCount,
                    AffectedElementCount = affectedElementCount,
                    NeedsReviewRuleCount = needsReviewRuleCount,
                    PassedRuleCount = passedRuleCount,
                    Findings = findings,
                    ScanSucceeded = true
                };
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation does not close the browser. The user may return to
                // the dashboard and continue the same authenticated session.
                throw;
            }
            catch (Exception exception)
            {
                SetProgress(
                    session,
                        isScanning: false,
                        stage: "Scan failed",
                        stagePercent: 100,
                        currentUrl: session.ActivePage?.Url,
                        currentPageNumber: session.NextStepNumber,
                        totalPageCount: null);

                _logger.LogError(
                    exception,
                    "Step {StepNumber} failed for authenticated audit session {SessionId}.",
                    stepNumber,
                    sessionId);

                // Preserve a failed scan attempt in the audit history. This makes it
                // clear that the rendered state was reached even if axe or page
                // inspection could not complete.
                stepResult = new AuthenticatedAuditStepResult
                {
                    StepNumber = stepNumber,
                    StepName = LimitLength(
                        GetStepName(snapshot, stepNumber),
                        200) ?? $"Step {stepNumber}",
                    Url = LimitLength(currentUrl, 2048) ?? session.StartingUrl,
                    PageTitle = LimitLength(snapshot?.PageTitle, 500),
                    Heading = LimitLength(snapshot?.Heading, 500),
                    DomFingerprint = snapshot is null
                        ? null
                        : CreateDomFingerprint(snapshot.FingerprintSource),
                    ScannedAt = DateTime.UtcNow,
                    VisibleFormCount = snapshot?.VisibleFormCount ?? 0,
                    VisibleFieldCount = snapshot?.VisibleFieldCount ?? 0,
                    VisibleButtonCount = snapshot?.VisibleButtonCount ?? 0,
                    ViolationRuleCount = 0,
                    AffectedElementCount = 0,
                    NeedsReviewRuleCount = 0,
                    PassedRuleCount = 0,
                    ScanSucceeded = false,
                    ErrorMessage = LimitLength(exception.Message, 4000)
                };
            }

            SetProgress(
                session,
                isScanning: true,
                stage: "Saving to database",
                stagePercent: 90,
                currentUrl: activePage.Url,
                currentPageNumber: session.NextStepNumber,
                totalPageCount: null);

            int savedStepId = await SaveAuditStepAsync(
                session.AuditRunId,
                stepResult,
                cancellationToken);

            SetProgress(
                session,
                isScanning: false,
                stage: "Complete",
                stagePercent: 100,
                currentUrl: activePage.Url,
                currentPageNumber: session.NextStepNumber,
                totalPageCount: null);

            session.LastSavedStepId = savedStepId;
            session.NextStepNumber++;

            _logger.LogInformation(
                "Saved authenticated audit step {StepNumber} for run {AuditRunId}. " +
                "Succeeded: {ScanSucceeded}.",
                stepResult.StepNumber,
                session.AuditRunId,
                stepResult.ScanSucceeded);

            return stepResult;
        }
        finally
        {
            session.OperationLock.Release();
        }
    }

    public async Task<AuthenticatedAuditBatchResult> ScanBatchAsync(
    Guid sessionId,
    IReadOnlyList<string> urls,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urls);

        Stopwatch batchStopwatch = Stopwatch.StartNew();

        /*
         * Ignore blank entries and trim surrounding spaces before validation.
         * Duplicate URLs are kept because the same URL may represent different
         * application states in some protected workflows.
         */
        string[] normalizedUrls =
            urls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .ToArray();

        if (normalizedUrls.Length == 0)
        {
            throw new ArgumentException(
                "Enter at least one authenticated URL.",
                nameof(urls));
        }

        if (normalizedUrls.Length > MaximumAuthenticatedBatchSize)
        {
            throw new ArgumentException(
                $"Authenticated batches are limited to " +
                $"{MaximumAuthenticatedBatchSize} URLs.",
                nameof(urls));
        }

        foreach (string url in normalizedUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri) ||
                (parsedUri.Scheme != Uri.UriSchemeHttp &&
                 parsedUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException(
                    $"The URL '{url}' is not a valid HTTP or HTTPS URL.",
                    nameof(urls));
            }
        }

        var itemResults =
            new List<AuthenticatedAuditBatchItemResult>(
                normalizedUrls.Length);

        foreach (string url in normalizedUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Stopwatch itemStopwatch = Stopwatch.StartNew();
            string? finalUrl = null;

            try
            {
                /*
                 * Navigate the Playwright page first, then reuse the normal
                 * single-step scanner. This keeps all accessibility collection,
                 * database persistence, and fingerprint logic in one place.
                 */
                finalUrl =
                    await NavigateAuthenticatedSessionAsync(
                        sessionId,
                        url,
                        cancellationToken);

                AuthenticatedAuditStepResult stepResult =
                    await ScanCurrentStepAsync(
                        sessionId,
                        cancellationToken);

                itemResults.Add(
                    new AuthenticatedAuditBatchItemResult
                    {
                        Url = url,
                        FinalUrl = finalUrl,
                        WasRedirected =
                            UrlChangedAfterNavigation(url, finalUrl),
                        StepNumber = stepResult.StepNumber,
                        StepName = stepResult.StepName,
                        Succeeded = stepResult.ScanSucceeded,
                        ViolationRuleCount =
                            stepResult.ViolationRuleCount,
                        NeedsReviewRuleCount =
                            stepResult.NeedsReviewRuleCount,
                        AffectedElementCount =
                            stepResult.AffectedElementCount,
                        DurationMilliseconds =
                            itemStopwatch.ElapsedMilliseconds,
                        ErrorMessage = stepResult.ErrorMessage
                    });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                /*
                 * One inaccessible or broken URL should not prevent the remaining
                 * protected pages from being scanned.
                 */
                _logger.LogWarning(
                    exception,
                    "Authenticated batch URL {Url} could not be scanned in " +
                    "session {SessionId}.",
                    url,
                    sessionId);

                itemResults.Add(
                    new AuthenticatedAuditBatchItemResult
                    {
                        Url = url,
                        Succeeded = false,
                        DurationMilliseconds =
                            itemStopwatch.ElapsedMilliseconds,
                        ErrorMessage =
                            LimitLength(exception.Message, 2000)
                    });
            }
        }

        batchStopwatch.Stop();
        return new AuthenticatedAuditBatchResult
        {
            RequestedCount = normalizedUrls.Length,
            SucceededCount =
                itemResults.Count(item => item.Succeeded),
            FailedCount =
                itemResults.Count(item => !item.Succeeded),
            DurationMilliseconds =
                batchStopwatch.ElapsedMilliseconds,
            Items = itemResults
        };
    }

    public async Task StopSessionAsync(
    Guid sessionId,
    bool markLastStepAsFinal,
    CancellationToken cancellationToken = default)
    {
        Stopwatch totalTimer = Stopwatch.StartNew();

        if (!_sessions.TryGetValue(
                sessionId,
                out AuthenticatedAuditBrowserSession? session))
        {
            throw new KeyNotFoundException(
                "The authenticated audit session was not found or is no longer running.");
        }

        Stopwatch lockTimer = Stopwatch.StartNew();

        await session.OperationLock.WaitAsync(cancellationToken);

        lockTimer.Stop();

        _logger.LogInformation(
            "Close browser timing: operation lock took {ElapsedMilliseconds} ms.",
            lockTimer.ElapsedMilliseconds);

        bool ownsShutdown = false;

        try
        {
            if (session.IsStopping)
            {
                throw new InvalidOperationException(
                    "The authenticated audit session is already stopping.");
            }

            /*
             * Remove the session so no new scans can use it while shutdown
             * is taking place.
             */
            if (!_sessions.TryRemove(sessionId, out _))
            {
                throw new InvalidOperationException(
                    "The authenticated audit session could not be stopped.");
            }

            session.IsStopping = true;
            ownsShutdown = true;

            /*
             * Save the completed status before disconnecting Playwright.
             * This prevents the audit history from remaining marked as Running.
             */
            try
            {
                Stopwatch databaseTimer = Stopwatch.StartNew();

                await CompleteAuditRunAsync(
                    session.AuditRunId,
                    session.LastSavedStepId,
                    markLastStepAsFinal);

                databaseTimer.Stop();

                _logger.LogInformation(
                    "Close browser timing: CompleteAuditRunAsync took " +
                    "{ElapsedMilliseconds} ms.",
                    databaseTimer.ElapsedMilliseconds);
            }
            finally
            {
                /*
                 * Always attempt to close the browser, even if the database
                 * update encounters a problem.
                 */
                Stopwatch browserShutdownTimer = Stopwatch.StartNew();

                try
                {
                    if (session.Browser is not null)
                    {
                        /*
                         * Close all open contexts and their pages first.
                         */
                        IBrowserContext[] openContexts =
                            session.Browser.Contexts.ToArray();

                        foreach (IBrowserContext context in openContexts)
                        {
                            try
                            {
                                await context.CloseAsync();
                            }
                            catch (PlaywrightException)
                            {
                                // The context may already be closed.
                            }
                            catch (ObjectDisposedException)
                            {
                                // The context connection may already be disposed.
                            }
                        }

                        /*
                         * Browser.CloseAsync takes approximately 30 seconds on
                         * this workstation. Use Chromium's CDP Browser.close
                         * command for immediate shutdown instead.
                         */
                        if (session.Browser.IsConnected)
                        {
                            try
                            {
                                ICDPSession cdpSession =
                                    await session.Browser
                                        .NewBrowserCDPSessionAsync();

                                await cdpSession.SendAsync("Browser.close");
                            }
                            catch (PlaywrightException)
                            {
                                /*
                                 * Browser.close disconnects Playwright, which can
                                 * throw even when the browser closed successfully.
                                 */
                            }
                            catch (ObjectDisposedException)
                            {
                                // The browser connection already closed.
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    /*
                     * Browser cleanup must not replace or prevent the database
                     * completion result.
                     */
                    _logger.LogWarning(
                        exception,
                        "The authenticated browser closed with a shutdown warning.");
                }
                finally
                {
                    browserShutdownTimer.Stop();

                    _logger.LogInformation(
                        "Close browser timing: browser shutdown took " +
                        "{ElapsedMilliseconds} ms.",
                        browserShutdownTimer.ElapsedMilliseconds);
                }
            }

            _logger.LogInformation(
                "Stopped authenticated audit session {SessionId} for run " +
                "{AuditRunId}. Last step marked final: {MarkLastStepAsFinal}.",
                sessionId,
                session.AuditRunId,
                markLastStepAsFinal);
        }
        finally
        {
            session.OperationLock.Release();

            if (ownsShutdown)
            {
                Stopwatch disposeTimer = Stopwatch.StartNew();

                try
                {
                    /*
                    * The browser was already closed through CDP above.
                    * Dispose only the remaining Playwright and lock resources.
                    */
                    await session.DisposeAsync(closeBrowser: false);
                }
                catch (PlaywrightException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Playwright was already disconnected during session disposal.");
                }
                catch (ObjectDisposedException)
                {
                    // The Playwright resources were already disposed.
                }

                disposeTimer.Stop();

                _logger.LogInformation(
                    "Close browser timing: DisposeAsync took " +
                    "{ElapsedMilliseconds} ms.",
                    disposeTimer.ElapsedMilliseconds);
            }

            totalTimer.Stop();

            _logger.LogInformation(
                "Close browser timing: total shutdown took " +
                "{ElapsedMilliseconds} ms.",
                totalTimer.ElapsedMilliseconds);
        }
    }


    public async Task InterruptAllSessionsAsync(
    CancellationToken cancellationToken = default)
    {
        /*
         * Take a snapshot because sessions will be removed from the concurrent
         * dictionary while this shutdown operation runs.
         */
        KeyValuePair<Guid, AuthenticatedAuditBrowserSession>[] sessions =
            _sessions.ToArray();

        foreach (KeyValuePair<Guid, AuthenticatedAuditBrowserSession> entry
                 in sessions)
        {
            Guid sessionId = entry.Key;
            AuthenticatedAuditBrowserSession session = entry.Value;

            bool lockTaken = false;
            bool ownsShutdown = false;

            try
            {
                /*
                 * Wait for a scan that is already running. This prevents the
                 * browser from closing halfway through an axe scan or SQL save.
                 */
                await session.OperationLock.WaitAsync(cancellationToken);
                lockTaken = true;

                if (session.IsStopping)
                {
                    continue;
                }

                /*
                 * Another request may have completed this session after the
                 * shutdown snapshot was created. Only the request that removes
                 * the session owns responsibility for closing its browser.
                 */
                bool removed =
                    _sessions.TryRemove(
                        sessionId,
                        out AuthenticatedAuditBrowserSession? removedSession);

                if (!removed ||
                    !ReferenceEquals(removedSession, session))
                {
                    continue;
                }

                session.IsStopping = true;
                ownsShutdown = true;

                await MarkRunAsInterruptedAsync(
                    session.AuditRunId,
                    "The dashboard application stopped before this authenticated " +
                    "audit session was completed. The Playwright browser was " +
                    "closed during application shutdown.");

                _logger.LogWarning(
                    "Interrupted authenticated audit session {SessionId} " +
                    "for run {AuditRunId} during application shutdown.",
                    sessionId,
                    session.AuditRunId);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Authenticated audit shutdown was cancelled before every " +
                    "browser session could be closed.");

                break;
            }
            catch (Exception exception)
            {
                /*
                 * One failed cleanup must not prevent the service from attempting
                 * to close the remaining browser sessions.
                 */
                _logger.LogError(
                    exception,
                    "Could not interrupt authenticated audit session {SessionId} " +
                    "during application shutdown.",
                    sessionId);
            }
            finally
            {
                if (lockTaken)
                {
                    session.OperationLock.Release();
                }

                if (ownsShutdown)
                {
                    try
                    {
                        await session.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(
                            exception,
                            "Could not completely close the Playwright browser " +
                            "for authenticated audit session {SessionId}.",
                            sessionId);
                    }
                }
            }
        }
    }

    private static IPage SelectPageForAudit(
    AuthenticatedAuditBrowserSession session)
    {
        List<IPage> openPages = session.BrowserContext.Pages
            .Where(page => !page.IsClosed)
            .ToList();

        if (openPages.Count == 0)
        {
            throw new InvalidOperationException(
                "The Playwright browser does not contain an open page.");
        }

        List<IPage> auditablePages = openPages
            .Where(IsAuditablePage)
            .ToList();

        if (auditablePages.Count == 0)
        {
            throw new InvalidOperationException(
                "No open HTTP or HTTPS application page was found.");
        }

        IPage? currentPage = session.ActivePage;

        /*
         * The login page is initially the first page in the browser context.
         * The protected BOE application may later open in a second tab.
         *
         * When that happens, select the newest usable tab. After it has been
         * selected once, keep reusing it so an unrelated popup does not
         * unexpectedly replace the application tab on later scans.
         */
        bool currentPageIsStillOpen =
            currentPage is not null
            && !currentPage.IsClosed
            && auditablePages.Contains(currentPage);

        if (currentPageIsStillOpen)
        {
            bool currentPageIsOriginalFirstPage =
                ReferenceEquals(currentPage, openPages[0]);

            if (!currentPageIsOriginalFirstPage || auditablePages.Count == 1)
            {
                return currentPage!;
            }
        }

        return auditablePages[^1];
    }

    private static bool IsAuditablePage(IPage page)
    {
        if (string.IsNullOrWhiteSpace(page.Url))
        {
            return false;
        }

        return Uri.TryCreate(
                   page.Url,
                   UriKind.Absolute,
                   out Uri? parsedUrl)
               && (parsedUrl.Scheme == Uri.UriSchemeHttp
                   || parsedUrl.Scheme == Uri.UriSchemeHttps);
    }

    private static string GetWcagTags(IEnumerable<string>? tags)
    {
        // Keep only WCAG-related tags instead of storing every axe category tag.
        return string.Join(", ",
            (tags ?? Enumerable.Empty<string>())
                .Where(tag =>
                    tag.StartsWith("wcag", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));
    }

    private static string? GetWcagLevel(IEnumerable<string>? tags)
    {
        var tagSet = new HashSet<string>(
            tags ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        // Check AA first so AA findings receive the correct staff-facing level.
        if (tagSet.Contains("wcag2aa") ||
            tagSet.Contains("wcag21aa") ||
            tagSet.Contains("wcag22aa"))
        {
            return "AA";
        }

        if (tagSet.Contains("wcag2a") ||
            tagSet.Contains("wcag21a") ||
            tagSet.Contains("wcag22a"))
        {
            return "A";
        }

        // Best-practice or non-WCAG rules do not receive an A/AA label.
        return null;
    }

    private async Task WaitForRenderedPageAsync(IPage page)
    {
        try
        {
            /*
             * DOMContentLoaded is used instead of NetworkIdle because protected
             * applications may keep background requests or connections open.
             *
             * This is only a short best-effort wait. The user already controls
             * when the Scan button is pressed.
             */
            await page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions
                {
                    Timeout = 10_000
                });
        }
        catch (System.TimeoutException)
        {
            _logger.LogWarning(
                "The page did not report DOMContentLoaded within the expected time. " +
                "The service will attempt to scan its current rendered state.");
        }
    }

    private static async Task<RenderedPageSnapshot>
        CaptureRenderedPageSnapshotAsync(IPage page)
    {
        const string snapshotScript = """
        () => {
            const cleanText = value =>
                (value || "")
                    .replace(/\s+/g, " ")
                    .trim();

            const isVisible = element => {
                if (!(element instanceof Element)) {
                    return false;
                }

                const style = window.getComputedStyle(element);

                if (
                    style.display === "none" ||
                    style.visibility === "hidden" ||
                    Number(style.opacity) === 0
                ) {
                    return false;
                }

                const rectangle = element.getBoundingClientRect();

                return rectangle.width > 0 && rectangle.height > 0;
            };

            const firstVisibleText = selector => {
                for (const element of document.querySelectorAll(selector)) {
                    if (!isVisible(element)) {
                        continue;
                    }

                    const text = cleanText(element.textContent);

                    if (text) {
                        return text;
                    }
                }

                return null;
            };

            const heading = firstVisibleText(
                "h1, h2, [role='heading'][aria-level='1'], " +
                "[role='heading'][aria-level='2']"
            );

            const stepName =
                firstVisibleText(
                    "[aria-current='step'], " +
                    "[role='tab'][aria-selected='true'], " +
                    ".step.active, " +
                    ".steps .active, " +
                    ".wizard-step.active"
                ) ||
                heading ||
                cleanText(document.title) ||
                null;

            const visibleFormCount =
                Array.from(document.querySelectorAll("form"))
                    .filter(isVisible)
                    .length;

            const visibleFieldCount =
                Array.from(
                    document.querySelectorAll(
                        "input:not([type='hidden']), " +
                        "select, textarea, [contenteditable='true']"
                    )
                )
                    .filter(isVisible)
                    .length;

            const visibleButtonCount =
                Array.from(
                    document.querySelectorAll(
                        "button, " +
                        "input[type='button'], " +
                        "input[type='submit'], " +
                        "input[type='reset'], " +
                        "[role='button']"
                    )
                )
                    .filter(isVisible)
                    .length;

            /*
             * Build a structural signature without reading form values.
             * This avoids including names, addresses, permit information,
             * or other user-entered data in the DOM fingerprint.
             */
            const signatureElements =
                Array.from(
                    document.querySelectorAll(
                        "form, h1, h2, h3, [role='heading'], label, " +
                        "input, select, textarea, button, [role='button'], " +
                        "[aria-current='step']"
                    )
                )
                    .slice(0, 1000);

            const structuralSignature =
                signatureElements
                    .map((element, index) => {
                        const tagName =
                            element.tagName.toLowerCase();

                        const safeText =
                            tagName === "input" ||
                            tagName === "select" ||
                            tagName === "textarea"
                                ? ""
                                : cleanText(element.textContent)
                                    .substring(0, 100);

                        return [
                            index,
                            tagName,
                            element.getAttribute("id") || "",
                            element.getAttribute("name") || "",
                            element.getAttribute("type") || "",
                            element.getAttribute("role") || "",
                            element.getAttribute("aria-label") || "",
                            element.getAttribute("aria-current") || "",
                            isVisible(element) ? "visible" : "hidden",
                            safeText
                        ].join("|");
                    })
                    .join("\n");

            return {
                PageTitle: cleanText(document.title) || null,
                Heading: heading,
                StepName: stepName,
                VisibleFormCount: visibleFormCount,
                VisibleFieldCount: visibleFieldCount,
                VisibleButtonCount: visibleButtonCount,

                // Use only the path, not the query string, because query
                // strings can contain tokens or private application data.
                FingerprintSource:
                    window.location.pathname +
                    "\n" +
                    structuralSignature
            };
        }
        """;

        RenderedPageSnapshot? snapshot =
            await page.EvaluateAsync<RenderedPageSnapshot>(
                snapshotScript);

        return snapshot
            ?? throw new InvalidOperationException(
                "The rendered page information could not be captured.");
    }


    private static string GetStepName(
        RenderedPageSnapshot? snapshot,
        int stepNumber)
    {
        if (!string.IsNullOrWhiteSpace(snapshot?.StepName))
        {
            return snapshot.StepName;
        }

        if (!string.IsNullOrWhiteSpace(snapshot?.Heading))
        {
            return snapshot.Heading;
        }

        if (!string.IsNullOrWhiteSpace(snapshot?.PageTitle))
        {
            return snapshot.PageTitle;
        }

        return $"Step {stepNumber}";
    }

    private static string CreateDomFingerprint(
        string? fingerprintSource)
    {
        string source = fingerprintSource ?? string.Empty;

        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        byte[] hashBytes = SHA256.HashData(sourceBytes);

        // SHA-256 produces a 64-character hexadecimal fingerprint.
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task<int> CreateAuditRunAsync(
        string applicationName,
        string startingUrl,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        // ApplicationDbContext is scoped. Because this service will live longer
        // than one web request, obtain a fresh scope for each database operation.
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var auditRun = new AuthenticatedAuditRun
        {
            ApplicationName = applicationName,
            StartingUrl = startingUrl,
            AccessibilityEngine = "axe-core",
            StartedAt = startedAt,
            Status = "Running"
        };

        dbContext.AuthenticatedAuditRuns.Add(auditRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        return auditRun.Id;
    }

    private static string? CreateFailureSummary(
    AxeResultNode node)
    {
        /*
         * The current axe .NET model does not provide one FailureSummary
         * property, so combine the useful messages from its node checks.
         */
        IEnumerable<string?> messages =
            (node.Any ?? Array.Empty<AxeResultCheck>())
                .Concat(node.All ?? Array.Empty<AxeResultCheck>())
                .Concat(node.None ?? Array.Empty<AxeResultCheck>())
                .Select(check => check.Message?.Trim());

        string summary = string.Join(
            Environment.NewLine,
            messages
                .Where(message =>
                    !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(summary)
            ? null
            : summary;
    }

    private static List<AuthenticatedAuditFindingResult>
    CreateFindingResults(AxeResult axeResult)
    {
        var findings = new List<AuthenticatedAuditFindingResult>();

        AddFindingResults(
            findings,
            axeResult.Violations,
            "Violation");

        /*
         * Axe's Incomplete collection contains results that could not be
         * conclusively determined and therefore require manual review.
         */
        AddFindingResults(
            findings,
            axeResult.Incomplete,
            "NeedsReview");

        return findings;
    }

    private static void AddFindingResults(
        ICollection<AuthenticatedAuditFindingResult> destination,
        IEnumerable<AxeResultItem>? axeItems,
        string findingType)
    {
        if (axeItems is null)
        {
            return;
        }

        foreach (AxeResultItem axeItem in axeItems)
        {
            string ruleId = axeItem.Id?.Trim() ?? string.Empty;

            /*
             * RuleId is required by our database model. An axe result without an
             * identifier cannot be stored reliably or linked to documentation.
             */
            if (string.IsNullOrWhiteSpace(ruleId))
            {
                continue;
            }

            destination.Add(
                new AuthenticatedAuditFindingResult
                {
                    FindingType =
                        LimitLength(findingType, 50)
                        ?? "Unknown",

                    RuleId =
                        LimitLength(ruleId, 200)
                        ?? ruleId,

                    Impact =
                        LimitLength(axeItem.Impact, 50),

                    Help =
                        LimitLength(axeItem.Help, 500),

                    Description =
                        LimitLength(axeItem.Description, 2000),

                    HelpUrl = axeItem.HelpUrl,

                    // Preserve WCAG metadata so findings can later be grouped and prioritized.
                    WcagTags =
                        GetWcagTags(axeItem.Tags),

                    WcagLevel =
                        GetWcagLevel(axeItem.Tags),

                    AffectedElementCount =
                        axeItem.Nodes?.Count() ?? 0,

                    // Keep the exact affected elements for details pages and reports.
                    Nodes =
                        axeItem.Nodes?
                            .Select(node =>
                                new AuthenticatedAuditFindingNodeResult
                                {
                                    Target =
                                        node.Target?.ToString()
                                        ?? string.Empty,

                                    Html =
                                        node.Html,

                                    FailureSummary =
                                        CreateFailureSummary(node)
                                })
                        .ToList()
                    ?? new List<AuthenticatedAuditFindingNodeResult>()
                    });
        }
    }

    private static void SetProgress(
    AuthenticatedAuditBrowserSession session,
    bool isScanning,
    string stage,
    int stagePercent,
    string? currentUrl,
    int currentPageNumber,
    int? totalPageCount)
    {
        session.Progress = new AuthenticatedAuditProgressResult
        {
            IsScanning = isScanning,
            Stage = stage,

            // Prevent an accidental value below 0 or above 100.
            StagePercent = Math.Clamp(stagePercent, 0, 100),

            CurrentUrl = currentUrl,
            CurrentPageNumber = currentPageNumber,
            TotalPageCount = totalPageCount
        };
    }

    private async Task<int> SaveAuditStepAsync(
    int auditRunId,
    AuthenticatedAuditStepResult stepResult,
    CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope =
            _scopeFactory.CreateAsyncScope();

        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var auditStep = new AuthenticatedAuditStep
        {
            AuthenticatedAuditRunId = auditRunId,
            StepNumber = stepResult.StepNumber,
            StepName = LimitLength(stepResult.StepName, 200)
                ?? $"Step {stepResult.StepNumber}",
            Url = LimitLength(stepResult.Url, 2048)
                ?? string.Empty,
            PageTitle = LimitLength(stepResult.PageTitle, 500),
            Heading = LimitLength(stepResult.Heading, 500),
            DomFingerprint = LimitLength(
                stepResult.DomFingerprint,
                128),
            ScannedAt = stepResult.ScannedAt,
            VisibleFormCount = stepResult.VisibleFormCount,
            VisibleFieldCount = stepResult.VisibleFieldCount,
            VisibleButtonCount = stepResult.VisibleButtonCount,
            ViolationRuleCount = stepResult.ViolationRuleCount,
            AffectedElementCount = stepResult.AffectedElementCount,
            NeedsReviewRuleCount = stepResult.NeedsReviewRuleCount,
            PassedRuleCount = stepResult.PassedRuleCount,
            ScanSucceeded = stepResult.ScanSucceeded,
            WasFinalStep = false,
            ErrorMessage = LimitLength(
                stepResult.ErrorMessage,
                4000)
        };
        /*
        * Add findings through the navigation collection before SaveChanges.
        * EF Core will insert the step first and automatically use its generated ID
        * as AuthenticatedAuditStepId for each related finding.
         */
        foreach (AuthenticatedAuditFindingResult findingResult
         in stepResult.Findings)
        {
            var auditFinding = new AuthenticatedAuditFinding
            {
                FindingType =
                    LimitLength(findingResult.FindingType, 50)
                    ?? "Unknown",

                RuleId =
                    LimitLength(findingResult.RuleId, 200)
                    ?? string.Empty,

                Impact =
                    LimitLength(findingResult.Impact, 50),

                Help =
                    LimitLength(findingResult.Help, 500),

                Description =
                    LimitLength(findingResult.Description, 2000),

                HelpUrl =
                    LimitLength(findingResult.HelpUrl, 2048),

                // Preserve WCAG metadata for filtering and prioritization.
                WcagTags =
                    LimitLength(findingResult.WcagTags, 1000)
                    ?? string.Empty,

                WcagLevel =
                    LimitLength(findingResult.WcagLevel, 10),

                AffectedElementCount =
                    findingResult.AffectedElementCount
            };

            foreach (AuthenticatedAuditFindingNodeResult nodeResult
                     in findingResult.Nodes)
            {
                auditFinding.Nodes.Add(
                    new AuthenticatedAuditFindingNode
                    {
                        Target =
                            LimitLength(nodeResult.Target, 2000)
                            ?? string.Empty,

                        Html =
                            LimitLength(nodeResult.Html, 10000),

                        FailureSummary =
                            LimitLength(nodeResult.FailureSummary, 4000)
                    });
            }

            auditStep.Findings.Add(auditFinding);
        }

        dbContext.AuthenticatedAuditSteps.Add(auditStep);
        await dbContext.SaveChangesAsync(cancellationToken);

        return auditStep.Id;
    }

    private async Task<string> NavigateAuthenticatedSessionAsync(
    Guid sessionId,
    string url,
    CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(
                sessionId,
                out AuthenticatedAuditBrowserSession? session))
        {
            throw new KeyNotFoundException(
                "The authenticated audit session is no longer running.");
        }

        bool lockTaken = false;

        try
        {
            /*
             * Prevent navigation from occurring while another request is scanning
             * or stopping the same browser session.
             */
            await session.OperationLock.WaitAsync(cancellationToken);
            lockTaken = true;

            if (session.IsStopping ||
                !_sessions.ContainsKey(sessionId))
            {
                throw new KeyNotFoundException(
                    "The authenticated audit session is no longer running.");
            }

            if (!session.Browser.IsConnected)
            {
                throw new KeyNotFoundException(
                    "The Playwright-controlled Edge browser has been closed.");
            }

            /*
             * Reuse the active page whenever possible. Every page created from the
             * same BrowserContext shares the user's authenticated cookies and
             * browser storage.
             */
            IPage? page = session.ActivePage;

            if (page is null || page.IsClosed)
            {
                page =
                    session.BrowserContext.Pages
                        .LastOrDefault(openPage => !openPage.IsClosed);

                page ??=
                    await session.BrowserContext.NewPageAsync();
            }

            await page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });

            session.ActivePage = page;

            /*
             * Some protected applications continue rendering after navigation.
             * This is best-effort because long-running application requests may
             * prevent a traditional network-idle state.
             */
            try
            {
                await page.WaitForLoadStateAsync(
                    LoadState.DOMContentLoaded,
                    new PageWaitForLoadStateOptions
                    {
                        Timeout = 10000
                    });
            }
            catch (System.TimeoutException)
            {
                _logger.LogDebug(
                    "Authenticated batch URL {Url} did not reach the requested " +
                    "load state before the timeout. The scan will still proceed.",
                    url);
            }
            /*
            * Playwright updates Page.Url after server-side redirects and client-side
            * navigation. Returning it lets the batch report unexpected destinations,
            * including possible sign-in redirects.
            */
            return page.Url;
        }
        finally
        {
            if (lockTaken)
            {
                session.OperationLock.Release();
            }
        }
    }

    private async Task CompleteAuditRunAsync(
    int auditRunId,
    int? lastSavedStepId,
    bool markLastStepAsFinal)
    {
        await using AsyncServiceScope scope =
            _scopeFactory.CreateAsyncScope();

        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        AuthenticatedAuditRun? auditRun =
            await dbContext.AuthenticatedAuditRuns.FindAsync(
                new object[] { auditRunId },
                CancellationToken.None);

        if (auditRun is null)
        {
            throw new InvalidOperationException(
                $"Authenticated audit run {auditRunId} could not be found.");
        }

        if (markLastStepAsFinal && lastSavedStepId.HasValue)
        {
            AuthenticatedAuditStep? lastStep =
                await dbContext.AuthenticatedAuditSteps.FindAsync(
                    new object[] { lastSavedStepId.Value },
                    CancellationToken.None);

            /*
             * Verify that the step actually belongs to this run before changing it.
             * This protects against marking an unrelated database record as final.
             */
            if (lastStep is not null
                && lastStep.AuthenticatedAuditRunId == auditRunId)
            {
                lastStep.WasFinalStep = true;
            }
        }

        auditRun.Status = "Completed";
        auditRun.CompletedAt = DateTime.UtcNow;
        auditRun.ErrorMessage = null;

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task HandleUnexpectedBrowserDisconnectAsync(
    Guid sessionId)
    {
        if (!_sessions.TryGetValue(
                sessionId,
                out AuthenticatedAuditBrowserSession? session))
        {
            /*
             * The normal Stop operation removes the session before closing Edge.
             * In that case, the Disconnected event requires no additional work.
             */
            return;
        }

        bool lockTaken = false;
        bool ownsCleanup = false;

        try
        {
            /*
             * If a scan was already running when Edge closed, wait for that
             * operation to finish handling its browser exception and database save.
             */
            await session.OperationLock.WaitAsync(CancellationToken.None);
            lockTaken = true;

            if (session.IsStopping)
            {
                return;
            }

            bool removed =
                _sessions.TryRemove(
                    sessionId,
                    out AuthenticatedAuditBrowserSession? removedSession);

            if (!removed ||
                !ReferenceEquals(removedSession, session))
            {
                return;
            }

            session.IsStopping = true;
            ownsCleanup = true;

            await MarkRunAsInterruptedAsync(
                session.AuditRunId,
                "The Playwright-controlled Edge browser was closed or " +
                "disconnected before this authenticated audit session was " +
                "completed.");

            _logger.LogWarning(
                "Authenticated audit session {SessionId} for run {AuditRunId} " +
                "was interrupted because its browser disconnected.",
                sessionId,
                session.AuditRunId);
        }
        catch (Exception exception)
        {
            /*
             * Browser disconnection handlers run outside a normal controller
             * request, so all exceptions must be caught and logged here.
             */
            _logger.LogError(
                exception,
                "Could not clean up disconnected authenticated audit session " +
                "{SessionId}.",
                sessionId);
        }
        finally
        {
            if (lockTaken)
            {
                session.OperationLock.Release();
            }

            if (ownsCleanup)
            {
                try
                {
                    /*
                     * Edge is already disconnected, but this still releases the
                     * remaining Playwright context and managed resources.
                     */
                    await session.DisposeAsync();
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Could not completely release disconnected Playwright " +
                        "session {SessionId}.",
                        sessionId);
                }
            }
        }
    }

    private void RegisterPageCloseTracking(
    Guid sessionId,
    IPage page)
    {
        /*
         * Page.Close is raised when the associated tab or browser window closes.
         * The event itself cannot be awaited, so cleanup runs through a protected
         * asynchronous helper.
         */
        page.Close += (_, _) =>
        {
            _ = HandlePossibleLastPageClosedAsync(sessionId);
        };
    }

    private async Task HandlePossibleLastPageClosedAsync(
        Guid sessionId)
    {
        /*
         * Give Playwright a brief moment to finish updating BrowserContext.Pages.
         * This also avoids interrupting the session during a transition where one
         * page closes immediately before another protected tab opens.
         */
        await Task.Delay(250);

        if (!_sessions.TryGetValue(
                sessionId,
                out AuthenticatedAuditBrowserSession? session))
        {
            return;
        }

        if (session.IsStopping)
        {
            return;
        }

        bool hasOpenAuditablePage =
            session.BrowserContext.Pages.Any(IsAuditablePage);

        if (hasOpenAuditablePage)
        {
            // Another login or protected application tab is still available.
            return;
        }

        /*
         * No usable Edge pages remain. Reuse the existing interruption cleanup,
         * which removes the session, updates SQL Server, and releases Playwright.
         */
        await HandleUnexpectedBrowserDisconnectAsync(sessionId);
    }

    private async Task MarkRunAsInterruptedAsync(
    int auditRunId,
    string errorMessage)
    {
        await using AsyncServiceScope scope =
            _scopeFactory.CreateAsyncScope();

        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        AuthenticatedAuditRun? auditRun =
            await dbContext.AuthenticatedAuditRuns.FindAsync(
                new object[] { auditRunId },
                CancellationToken.None);

        if (auditRun is null)
        {
            return;
        }

        /*
         * Do not overwrite a run that another request successfully completed
         * while application shutdown was beginning.
         */
        if (auditRun.Status != "Running")
        {
            return;
        }

        auditRun.Status = "Interrupted";
        auditRun.CompletedAt = DateTime.UtcNow;
        auditRun.ErrorMessage = LimitLength(errorMessage, 4000);

        /*
         * Once shutdown owns the session, record its final state even when the
         * original HTTP request or application cancellation token is cancelled.
         */
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MarkRunAsUnsuccessfulAsync(
        int auditRunId,
        string status,
        string? errorMessage)
    {
        try
        {
            await using AsyncServiceScope scope =
                _scopeFactory.CreateAsyncScope();

            ApplicationDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            AuthenticatedAuditRun? auditRun =
                await dbContext.AuthenticatedAuditRuns.FindAsync(
                    new object[] { auditRunId },
                    CancellationToken.None);

            if (auditRun is null)
            {
                return;
            }

            auditRun.Status = status;
            auditRun.CompletedAt = DateTime.UtcNow;
            auditRun.ErrorMessage = LimitLength(errorMessage, 4000);

            // Do not use the original cancellation token here. Even if the
            // request was cancelled, we still want to record what happened.
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception databaseException)
        {
            // A database logging failure should not hide the original
            // Playwright startup exception.
            _logger.LogError(
                databaseException,
                "Could not update unsuccessful audit run {AuditRunId}.",
                auditRunId);
        }
    }

    private static void ValidateStartRequest(
        string applicationName,
        string startingUrl)
    {
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            throw new ArgumentException(
                "An application name is required.",
                nameof(applicationName));
        }

        if (applicationName.Length > 200)
        {
            throw new ArgumentException(
                "The application name cannot exceed 200 characters.",
                nameof(applicationName));
        }

        if (startingUrl.Length > 2048)
        {
            throw new ArgumentException(
                "The starting URL cannot exceed 2,048 characters.",
                nameof(startingUrl));
        }

        bool isValidUrl =
            Uri.TryCreate(startingUrl, UriKind.Absolute, out Uri? parsedUrl)
            && (parsedUrl.Scheme == Uri.UriSchemeHttps
                || parsedUrl.Scheme == Uri.UriSchemeHttp);

        if (!isValidUrl)
        {
            throw new ArgumentException(
                "Enter a valid absolute HTTP or HTTPS URL.",
                nameof(startingUrl));
        }
    }

    private static async Task ClosePartiallyCreatedBrowserAsync(
        IBrowserContext? browserContext,
        IBrowser? browser,
        IPlaywright? playwright)
    {
        if (browserContext is not null)
        {
            try
            {
                await browserContext.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // The browser may already have closed the context.
            }
        }

        if (browser is not null)
        {
            try
            {
                await browser.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // The browser may already have been closed manually.
            }
        }

        playwright?.Dispose();
    }

    private static string? LimitLength(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
        {
            return value;
        }

        return value[..maximumLength];
    }

    /// <summary>
    /// Compares the requested and final navigation destinations while ignoring
    /// query strings and fragments, which protected applications commonly change.
    /// </summary>
    private static bool UrlChangedAfterNavigation(
        string requestedUrl,
        string finalUrl)
    {
        if (!Uri.TryCreate(
                requestedUrl,
                UriKind.Absolute,
                out Uri? requestedUri) ||
            !Uri.TryCreate(
                finalUrl,
                UriKind.Absolute,
                out Uri? finalUri))
        {
            return !string.Equals(
                requestedUrl.Trim(),
                finalUrl.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        string requestedPath =
            requestedUri.AbsolutePath.TrimEnd('/');

        string finalPath =
            finalUri.AbsolutePath.TrimEnd('/');

        return
            !string.Equals(
                requestedUri.Scheme,
                finalUri.Scheme,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                requestedUri.Host,
                finalUri.Host,
                StringComparison.OrdinalIgnoreCase) ||
            requestedUri.Port != finalUri.Port ||
            !string.Equals(
                requestedPath,
                finalPath,
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Temporary rendered-page information captured from JavaScript.
    ///
    /// This object is never saved directly. Only the selected counts, labels,
    /// and SHA-256 fingerprint are stored in SQL Server.
    /// </summary>
    private sealed class RenderedPageSnapshot
    {
        public string? PageTitle { get; set; }

        public string? Heading { get; set; }

        public string? StepName { get; set; }

        public int VisibleFormCount { get; set; }

        public int VisibleFieldCount { get; set; }

        public int VisibleButtonCount { get; set; }

        public string? FingerprintSource { get; set; }
    }
}
