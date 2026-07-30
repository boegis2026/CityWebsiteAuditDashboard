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

    public async Task<AuthenticatedAuditNavigationAnalysisResult>
    AnalyzeCurrentStateAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(
            sessionId,
            out AuthenticatedAuditBrowserSession? session))
        {
            throw new KeyNotFoundException(
                "The authenticated audit session was not found.");
        }

        await session.OperationLock.WaitAsync(cancellationToken);

        try
        {
            if (session.IsStopping ||
                !_sessions.ContainsKey(sessionId))
            {
                throw new KeyNotFoundException(
                    "The authenticated audit session is no longer active.");
            }

            if (!session.Browser.IsConnected)
            {
                throw new InvalidOperationException(
                    "The authenticated browser is no longer connected.");
            }

            /*
             * Reuse the same page-selection behavior as manual scanning.
             * This supports protected applications that opened in a newer tab.
             */
            IPage activePage =
                SelectPageForAudit(session);

            session.ActivePage = activePage;

            await activePage.BringToFrontAsync();

            return await AnalyzePageForAutomaticNavigationAsync(
                activePage);
        }
        finally
        {
            session.OperationLock.Release();
        }
    }

    public bool RequestAutomaticWorkflowStop(
    Guid sessionId)
    {
        if (!_sessions.TryGetValue(
            sessionId,
            out AuthenticatedAuditBrowserSession? session))
        {
            throw new KeyNotFoundException(
                "The authenticated audit session was not found.");
        }

        return session.RequestAutomaticWorkflowStop();
    }

    public async Task<AuthenticatedAuditAutomaticRunResult>
    RunAutomaticWorkflowAsync(
        Guid sessionId,
        int maximumStateCount = 25,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(
            sessionId,
            out AuthenticatedAuditBrowserSession? session))
        {
            throw new KeyNotFoundException(
                "The authenticated audit session was not found.");
        }

        if (!session.TryStartAutomaticWorkflow(
            out CancellationToken sessionCancellationToken))
        {
            throw new InvalidOperationException(
                "An automatic workflow is already running for this session.");
        }

        /*
         * Stop when either the dashboard request is canceled or the user
         * explicitly requests that the automatic workflow stop.
         */
        using CancellationTokenSource linkedCancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                sessionCancellationToken);

        CancellationToken workflowCancellationToken =
            linkedCancellationSource.Token;

        /*
         * Prevent an invalid or excessive automatic run.
         * Twenty-five states is the proof-of-concept safety limit.
         */
        int safeMaximumStateCount =
            Math.Clamp(
                maximumStateCount,
                1,
                25);

        var cycles =
            new List<AuthenticatedAuditAutomaticCycleResult>();

        /*
         * Contains only states that have already been scanned.
         * This prevents the navigator from repeatedly cycling through
         * an earlier workflow state.
         */
        var scannedStateSignatures =
            new HashSet<string>(
                StringComparer.Ordinal);

        int advancedStateCount = 0;
        string? finalUrl = null;

        try
        {
            for (int stateIndex = 0;
                stateIndex < safeMaximumStateCount;
                stateIndex++)
            {
                workflowCancellationToken.ThrowIfCancellationRequested();

                /*
                 * Scan the last permitted state, but do not click Next.
                 * This prevents automation from entering an additional
                 * unscanned state after reaching the configured limit.
                 */
                if (stateIndex == safeMaximumStateCount - 1)
                {
                    AuthenticatedAuditStepResult finalScanResult =
                        await ScanCurrentStepAsync(
                            sessionId,
                            workflowCancellationToken);

                    var limitNavigationResult =
                        new AuthenticatedAuditAutomaticNavigationResult
                        {
                            Status = "MaximumReached",

                            NavigationAttempted = false,
                            Navigated = false,

                            RequiresManualInteraction = false,

                            Message =
                                "The final permitted state was scanned. " +
                                "No additional navigation was attempted.",

                            StopReason =
                                "The configured maximum state count was reached."
                        };

                    cycles.Add(
                        new AuthenticatedAuditAutomaticCycleResult
                        {
                            ScanSucceeded = true,

                            ScannedStepNumber =
                                finalScanResult.StepNumber,

                            ScanResult =
                                finalScanResult,

                            NavigationResult =
                                limitNavigationResult,

                            Message =
                                "The state was scanned without advancing."
                        });

                    return new AuthenticatedAuditAutomaticRunResult
                    {
                        Status = "MaximumReached",

                        ScannedStateCount =
                            cycles.Count,

                        AdvancedStateCount =
                            advancedStateCount,

                        MaximumStateCount =
                            safeMaximumStateCount,

                        ReachedMaximumStateCount = true,

                        FinalUrl =
                            finalUrl,

                        Message =
                            $"The automatic workflow stopped after scanning " +
                            $"the maximum of {safeMaximumStateCount} states.",

                        StopReason =
                            "The safety limit was reached. The browser remains " +
                            "on the final scanned state.",

                        Cycles =
                            cycles
                    };
                }

                AuthenticatedAuditAutomaticCycleResult cycle =
                    await ScanAndAdvanceAutomaticStepAsync(
                        sessionId,
                        workflowCancellationToken);

                cycles.Add(cycle);

                AuthenticatedAuditAutomaticNavigationResult?
                    navigation =
                        cycle.NavigationResult;

                if (navigation is null)
                {
                    return new AuthenticatedAuditAutomaticRunResult
                    {
                        Status = "Failed",

                        ScannedStateCount =
                            cycles.Count,

                        AdvancedStateCount =
                            advancedStateCount,

                        MaximumStateCount =
                            safeMaximumStateCount,

                        ReachedMaximumStateCount = false,

                        FinalUrl =
                            finalUrl,

                        Message =
                            "The current state was scanned, but no navigation result was returned.",

                        StopReason =
                            "The automatic cycle returned an incomplete result.",

                        Cycles =
                            cycles
                    };
                }

                /*
                * Dynamic workflow pages may render before their validation and
                * navigation handlers are completely ready. Retry the advance a
                * few times without rescanning or creating duplicate states.
                */
                if (string.Equals(
                    navigation.Status,
                    "NoStateChange",
                    StringComparison.OrdinalIgnoreCase))
                {
                    const int maximumAdvanceRetries = 3;

                    for (
                        int retryAttempt = 1;
                        retryAttempt <= maximumAdvanceRetries;
                        retryAttempt++)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(2500),
                            workflowCancellationToken);

                        AuthenticatedAuditAutomaticNavigationResult
                            retryNavigation =
                                await AdvanceAutomaticStepAsync(
                                    sessionId,
                                    workflowCancellationToken);

                        cycle =
                            new AuthenticatedAuditAutomaticCycleResult
                            {
                                ScanSucceeded =
                                    cycle.ScanSucceeded,

                                ScannedStepNumber =
                                    cycle.ScannedStepNumber,

                                ScanResult =
                                    cycle.ScanResult,

                                NavigationResult =
                                    retryNavigation,

                                Message =
                                    retryNavigation.Message
                            };

                        /*
                         * Replace the failed navigation attempt. Do not add another
                         * scanned state because the page was already scanned.
                         */
                        cycles[cycles.Count - 1] =
                            cycle;

                        navigation =
                            retryNavigation;

                        if (!string.Equals(
                            navigation.Status,
                            "NoStateChange",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }
                }

                finalUrl =
                    navigation.UrlAfter ??
                    navigation.UrlBefore ??
                    finalUrl;

                if (!string.IsNullOrWhiteSpace(
                                navigation.StateSignatureBefore))
                {
                    scannedStateSignatures.Add(
                        navigation.StateSignatureBefore);
                }

                if (navigation.Navigated)
                {
                    advancedStateCount++;

                    /*
                    * The destination has not been scanned during this cycle yet.
                    * If its signature already exists, the workflow returned to an
                    * earlier scanned state and should stop before scanning it again.
                    */
                    if (
                        !string.IsNullOrWhiteSpace(
                            navigation.StateSignatureAfter) &&
                        scannedStateSignatures.Contains(
                            navigation.StateSignatureAfter))
                    {
                        return new AuthenticatedAuditAutomaticRunResult
                        {
                            Status = "LoopDetected",

                            ScannedStateCount =
                                cycles.Count,

                            AdvancedStateCount =
                                advancedStateCount,

                            MaximumStateCount =
                                safeMaximumStateCount,

                            ReachedMaximumStateCount = false,

                            FinalUrl =
                                finalUrl,

                            Message =
                                "Automatic navigation stopped because the workflow returned to a previously scanned state.",

                            StopReason =
                                "A repeated rendered state was detected. The browser remains open for manual review.",

                            Cycles =
                                cycles
                        };
                    }

                    /*
                    * Some multi-step applications update the visible state before their
                    * JavaScript controls and validation logic have finished initializing.
                    * Allow the new rendered state to settle before scanning and filling it.
                    */
                    await Task.Delay(
                        TimeSpan.FromSeconds(2),
                        workflowCancellationToken);

                    continue;

                }

                /*
                 * A static page or a state with no additional safe action
                 * is a normal completion. The current state was still
                 * scanned and saved before navigation stopped.
                 */
                bool completedNormally =
                    string.Equals(
                        navigation.Status,
                        "StaticPage",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        navigation.Status,
                        "NoSafeAction",
                        StringComparison.OrdinalIgnoreCase);

                if (completedNormally)
                {
                    return new AuthenticatedAuditAutomaticRunResult
                    {
                        Status = "Completed",

                        ScannedStateCount =
                            cycles.Count,

                        AdvancedStateCount =
                            advancedStateCount,

                        MaximumStateCount =
                            safeMaximumStateCount,

                        ReachedMaximumStateCount = false,

                        FinalUrl =
                            finalUrl,

                        Message =
                            "The supported workflow states were scanned and no additional safe navigation action was found.",

                        StopReason =
                            navigation.StopReason,

                        Cycles =
                            cycles
                    };
                }

                /*
                 * Validation failures, unsupported fields, authentication,
                 * CAPTCHA, final actions, and other uncertain states stop
                 * automation while leaving the browser open for manual use.
                 */
                return new AuthenticatedAuditAutomaticRunResult
                {
                    Status = "ManualActionRequired",

                    ScannedStateCount =
                        cycles.Count,

                    AdvancedStateCount =
                        advancedStateCount,

                    MaximumStateCount =
                        safeMaximumStateCount,

                    ReachedMaximumStateCount = false,

                    FinalUrl =
                        finalUrl,

                    Message =
                        "Automatic navigation stopped. The browser remains open so the workflow can continue manually.",

                    StopReason =
                        navigation.StopReason ??
                        navigation.Message,

                    Cycles =
                        cycles
                };
            }

            /*
             * The loop should normally return from one of the conditions
             * above, but this protects against an unexpected fall-through.
             */
            return new AuthenticatedAuditAutomaticRunResult
            {
                Status = "MaximumReached",

                ScannedStateCount =
                    cycles.Count,

                AdvancedStateCount =
                    advancedStateCount,

                MaximumStateCount =
                    safeMaximumStateCount,

                ReachedMaximumStateCount = true,

                FinalUrl =
                    finalUrl,

                Message =
                    "The automatic workflow reached its configured state limit.",

                StopReason =
                    "The safety limit was reached.",

                Cycles =
                    cycles
            };
        }
        catch (OperationCanceledException)
        {
            return new AuthenticatedAuditAutomaticRunResult
            {
                Status = "StoppedByUser",

                ScannedStateCount =
                    cycles.Count,

                AdvancedStateCount =
                    advancedStateCount,

                MaximumStateCount =
                    safeMaximumStateCount,

                ReachedMaximumStateCount = false,

                FinalUrl =
                    finalUrl,

                Message =
                    "The automatic workflow was stopped. " +
                    "The authenticated browser remains open.",

                StopReason =
                    "The user requested that automatic navigation stop.",

                Cycles =
                    cycles
            };
        }
        catch (Exception exception)
        {
            return new AuthenticatedAuditAutomaticRunResult
            {
                Status = "Failed",

                ScannedStateCount =
                    cycles.Count,

                AdvancedStateCount =
                    advancedStateCount,

                MaximumStateCount =
                    safeMaximumStateCount,

                ReachedMaximumStateCount = false,

                FinalUrl =
                    finalUrl,

                Message =
                    "The automatic workflow encountered an unexpected error.",

                StopReason =
                    exception.Message,

                Cycles =
                    cycles
            };
        }

        finally
        {
            /*
             * Release the session even when the workflow completes,
             * stops for manual input, reaches its limit, is canceled,
             * or encounters an exception.
             */
            session.FinishAutomaticWorkflow();
        }
    }

    public async Task<AuthenticatedAuditAutomaticCycleResult>
    ScanAndAdvanceAutomaticStepAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        /*
         * Each existing operation manages the session lock itself.
         * Do not acquire OperationLock here or the calls would deadlock.
         */
        AuthenticatedAuditStepResult scanResult =
            await ScanCurrentStepAsync(
                sessionId,
                cancellationToken);

        AuthenticatedAuditAutomaticNavigationResult navigationResult =
            await AdvanceAutomaticStepAsync(
                sessionId,
                cancellationToken);

        return new AuthenticatedAuditAutomaticCycleResult
        {
            ScanSucceeded = true,

            ScannedStepNumber =
                scanResult.StepNumber,

            ScanResult =
                scanResult,

            NavigationResult =
                navigationResult,

            Message =
                navigationResult.Navigated
                    ? "The current state was scanned and the workflow advanced."
                    : "The current state was scanned, but automatic navigation stopped."
        };
    }

    public async Task<AuthenticatedAuditAutomaticNavigationResult>
    AdvanceAutomaticStepAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(
            sessionId,
            out AuthenticatedAuditBrowserSession? session))
        {
            throw new KeyNotFoundException(
                "The authenticated audit session was not found.");
        }

        await session.OperationLock.WaitAsync(cancellationToken);

        try
        {
            if (session.IsStopping ||
                !_sessions.ContainsKey(sessionId))
            {
                throw new KeyNotFoundException(
                    "The authenticated audit session is no longer active.");
            }

            if (!session.Browser.IsConnected)
            {
                throw new InvalidOperationException(
                    "The authenticated browser is no longer connected.");
            }

            IPage activePage =
                SelectPageForAudit(session);

            session.ActivePage = activePage;

            await activePage.BringToFrontAsync();

            string urlBefore =
                activePage.Url;

            AuthenticatedAuditNavigationAnalysisResult analysis =
                await AnalyzePageForAutomaticNavigationAsync(
                    activePage);

            /*
             * Unsafe pages such as authentication, CAPTCHA, payment, or file
             * upload pages must still stop before anything is filled.
             */
            if (string.Equals(
                analysis.PageType,
                "Unsupported",
                StringComparison.OrdinalIgnoreCase))
            {
                return new AuthenticatedAuditAutomaticNavigationResult
                {
                    Status = "ManualActionRequired",

                    UrlBefore = urlBefore,
                    UrlAfter = activePage.Url,

                    NavigationAttempted = false,
                    Navigated = false,

                    RequiresManualInteraction = true,

                    Message =
                        analysis.RecommendedAction,

                    StopReason =
                        analysis.StopReason
                };
            }

            AuthenticatedAuditFieldFillResult fillResult =
                await FillSafeFieldsAsync(activePage);

            if (fillResult.RequiresManualInteraction)
            {
                return new AuthenticatedAuditAutomaticNavigationResult
                {
                    Status = "ManualActionRequired",

                    UrlBefore = urlBefore,
                    UrlAfter = activePage.Url,

                    FilledFieldCount =
                        fillResult.FilledFieldCount,

                    SkippedFieldCount =
                        fillResult.SkippedFieldCount,

                    NavigationAttempted = false,
                    Navigated = false,

                    RequiresManualInteraction = true,

                    Message =
                        "Automatic navigation stopped before clicking anything.",

                    StopReason =
                        fillResult.StopReason
                };
            }

            /*
            * Some JavaScript applications enable their Next button only after
            * change, focus-out, and validation processing have completed.
            */
            await activePage.EvaluateAsync(
                """
                () => {
                    const activeElement =
                        document.activeElement;

                    if (activeElement instanceof HTMLElement) {
                        activeElement.blur();
                    }
                }
                """);

            await Task.Delay(
                TimeSpan.FromSeconds(2),
                cancellationToken);

            /*
             * Only treat it as a static page after attempting to inspect/fill it.
             * Some application screens contain controls without a normal <form>.
             */
            if (
                string.Equals(
                    analysis.PageType,
                    "StaticPage",
                    StringComparison.OrdinalIgnoreCase) &&
                fillResult.FilledFieldCount == 0 &&
                fillResult.SkippedFieldCount == 0)
            {
                return new AuthenticatedAuditAutomaticNavigationResult
                {
                    Status = "StaticPage",
                    UrlBefore = urlBefore,
                    UrlAfter = activePage.Url,
                    NavigationAttempted = false,
                    Navigated = false,
                    RequiresManualInteraction = false,
                    Message =
                        "No fillable form controls or safe workflow action were detected."
                };
            }

            AuthenticatedAuditNextActionResult nextAction =
                await FindSafeNextActionAsync(activePage);

            if (!nextAction.Found ||
                string.IsNullOrWhiteSpace(nextAction.Selector))
            {
                return new AuthenticatedAuditAutomaticNavigationResult
                {
                    Status =
                        nextAction.RequiresManualInteraction
                            ? "ManualActionRequired"
                            : "NoSafeAction",

                    UrlBefore = urlBefore,
                    UrlAfter = activePage.Url,

                    FilledFieldCount =
                        fillResult.FilledFieldCount,

                    SkippedFieldCount =
                        fillResult.SkippedFieldCount,

                    NavigationAttempted = false,
                    Navigated = false,

                    RequiresManualInteraction =
                        nextAction.RequiresManualInteraction,

                    Message =
                        "No safe automatic navigation action was selected.",

                    StopReason =
                        nextAction.StopReason
                };
            }

            /*
             * Check a link or form destination before clicking. Automatic
             * navigation must not leave the current website origin.
             */
            string? intendedDestination =
                await activePage.EvaluateAsync<string?>(
                    """
                () => {
                    const action =
                        document.querySelector(
                            '[data-city-audit-next-action="true"]');

                    if (!action) {
                        return null;
                    }

                    if (
                        action instanceof HTMLAnchorElement &&
                        action.href) {
                        return action.href;
                    }

                    const form =
                        action.closest("form");

                    return form?.action || null;
                }
                """);

            if (
                !string.IsNullOrWhiteSpace(
                    intendedDestination) &&
                Uri.TryCreate(
                    urlBefore,
                    UriKind.Absolute,
                    out Uri? currentUri) &&
                Uri.TryCreate(
                    intendedDestination,
                    UriKind.Absolute,
                    out Uri? destinationUri))
            {
                bool sameOrigin =
                    string.Equals(
                        currentUri.Scheme,
                        destinationUri.Scheme,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        currentUri.Host,
                        destinationUri.Host,
                        StringComparison.OrdinalIgnoreCase) &&
                    currentUri.Port ==
                        destinationUri.Port;

                if (!sameOrigin)
                {
                    return new AuthenticatedAuditAutomaticNavigationResult
                    {
                        Status = "ManualActionRequired",

                        UrlBefore = urlBefore,
                        UrlAfter = activePage.Url,

                        FilledFieldCount =
                            fillResult.FilledFieldCount,

                        SkippedFieldCount =
                            fillResult.SkippedFieldCount,

                        ActionText =
                            nextAction.ActionText,

                        NavigationAttempted = false,
                        Navigated = false,

                        RequiresManualInteraction = true,

                        Message =
                            "Automatic navigation did not click the action.",

                        StopReason =
                            "The selected action would leave the current website origin."
                    };
                }
            }

            string signatureBefore =
                await GetAutomaticNavigationStateSignatureAsync(
                    activePage);

            ILocator actionLocator =
                activePage.Locator(
                    nextAction.Selector);

            int matchingActionCount =
                await actionLocator.CountAsync();

            if (matchingActionCount != 1)
            {
                return new AuthenticatedAuditAutomaticNavigationResult
                {
                    Status = "ManualActionRequired",

                    UrlBefore = urlBefore,
                    UrlAfter = activePage.Url,

                    FilledFieldCount =
                        fillResult.FilledFieldCount,

                    SkippedFieldCount =
                        fillResult.SkippedFieldCount,

                    ActionText =
                        nextAction.ActionText,

                    NavigationAttempted = false,
                    Navigated = false,

                    RequiresManualInteraction = true,

                    Message =
                        "The action was not clicked.",

                    StopReason =
                        "The selected Next action changed or was no longer unique."
                };
            }

            await actionLocator.ScrollIntoViewIfNeededAsync();

            await actionLocator.ClickAsync(
                new LocatorClickOptions
                {
                    Timeout = 15000
                });

            bool renderedStateChanged = false;

            string signatureAfter =
                signatureBefore;

            IPage pageAfter =
                activePage;

            /*
             * Poll because many multi-step forms update the DOM without
             * changing the URL or performing a normal page navigation.
             */
            for (int attempt = 0;
                 attempt < 20;
                 attempt++)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(500),
                    cancellationToken);

                try
                {
                    pageAfter =
                        SelectPageForAudit(session);

                    session.ActivePage =
                        pageAfter;

                    signatureAfter =
                        await GetAutomaticNavigationStateSignatureAsync(
                            pageAfter);

                    if (!string.Equals(
                        signatureBefore,
                        signatureAfter,
                        StringComparison.Ordinal))
                    {
                        renderedStateChanged = true;
                        break;
                    }
                }
                catch (PlaywrightException)
                {
                    /*
                     * The old page may be temporarily unavailable while
                     * navigation or a new browser tab is being created.
                     */
                }
            }

            await pageAfter.BringToFrontAsync();

            return new AuthenticatedAuditAutomaticNavigationResult
            {
                Status =
                    renderedStateChanged
                        ? "Advanced"
                        : "NoStateChange",

                UrlBefore = urlBefore,
                UrlAfter = pageAfter.Url,

                StateSignatureBefore =
                    signatureBefore,

                StateSignatureAfter =
                    signatureAfter,

                FilledFieldCount =
                    fillResult.FilledFieldCount,

                SkippedFieldCount =
                    fillResult.SkippedFieldCount,

                ActionText =
                    nextAction.ActionText,

                NavigationAttempted = true,

                Navigated =
                    renderedStateChanged,

                RequiresManualInteraction =
                    !renderedStateChanged,

                Message =
                    renderedStateChanged
                        ? "The workflow advanced to a new rendered state."
                        : "The action was clicked, but no new rendered state was detected.",

                StopReason =
                    renderedStateChanged
                        ? null
                        : "The form may have validation errors or require manual input."
            };
        }
        finally
        {
            session.OperationLock.Release();
        }
    }

    public async Task<AuthenticatedAuditAutomaticNavigationResult>
    PreviewAutomaticStepAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(
            sessionId,
            out AuthenticatedAuditBrowserSession? session))
        {
            throw new KeyNotFoundException(
                "The authenticated audit session was not found.");
        }

        await session.OperationLock.WaitAsync(cancellationToken);

        try
        {
            if (session.IsStopping ||
                !_sessions.ContainsKey(sessionId))
            {
                throw new KeyNotFoundException(
                    "The authenticated audit session is no longer active.");
            }

            if (!session.Browser.IsConnected)
            {
                throw new InvalidOperationException(
                    "The authenticated browser is no longer connected.");
            }

            IPage activePage =
                SelectPageForAudit(session);

            session.ActivePage = activePage;

            await activePage.BringToFrontAsync();

            string urlBefore =
                activePage.Url;

            AuthenticatedAuditNavigationAnalysisResult analysis =
                await AnalyzePageForAutomaticNavigationAsync(
                    activePage);

            /*
             * A static page is a normal result. It can be scanned once,
             * but there is no workflow action to advance.
             */
            if (!analysis.CanNavigateAutomatically)
            {
                bool requiresManualInteraction =
                    !string.Equals(
                        analysis.PageType,
                        "StaticPage",
                        StringComparison.OrdinalIgnoreCase);

                return new AuthenticatedAuditAutomaticNavigationResult
                {
                    Status =
                        requiresManualInteraction
                            ? "ManualActionRequired"
                            : "StaticPage",

                    UrlBefore = urlBefore,
                    UrlAfter = activePage.Url,

                    NavigationAttempted = false,
                    Navigated = false,

                    RequiresManualInteraction =
                        requiresManualInteraction,

                    Message =
                        analysis.RecommendedAction,

                    StopReason =
                        analysis.StopReason
                };
            }

            AuthenticatedAuditFieldFillResult fillResult =
                await FillSafeFieldsAsync(activePage);

            if (fillResult.RequiresManualInteraction)
            {
                return new AuthenticatedAuditAutomaticNavigationResult
                {
                    Status = "ManualActionRequired",

                    UrlBefore = urlBefore,
                    UrlAfter = activePage.Url,

                    FilledFieldCount =
                        fillResult.FilledFieldCount,

                    SkippedFieldCount =
                        fillResult.SkippedFieldCount,

                    NavigationAttempted = false,
                    Navigated = false,

                    RequiresManualInteraction = true,

                    Message =
                        "The supported fields were inspected, but manual interaction is required.",

                    StopReason =
                        fillResult.StopReason
                };
            }

            AuthenticatedAuditNextActionResult nextAction =
                await FindSafeNextActionAsync(activePage);

            if (!nextAction.Found)
            {
                return new AuthenticatedAuditAutomaticNavigationResult
                {
                    Status =
                        nextAction.RequiresManualInteraction
                            ? "ManualActionRequired"
                            : "NoSafeAction",

                    UrlBefore = urlBefore,
                    UrlAfter = activePage.Url,

                    FilledFieldCount =
                        fillResult.FilledFieldCount,

                    SkippedFieldCount =
                        fillResult.SkippedFieldCount,

                    NavigationAttempted = false,
                    Navigated = false,

                    RequiresManualInteraction =
                        nextAction.RequiresManualInteraction,

                    Message =
                        "The current fields were processed, but no safe navigation action was selected.",

                    StopReason =
                        nextAction.StopReason
                };
            }

            return new AuthenticatedAuditAutomaticNavigationResult
            {
                Status = "ReadyToAdvance",

                UrlBefore = urlBefore,
                UrlAfter = activePage.Url,

                FilledFieldCount =
                    fillResult.FilledFieldCount,

                SkippedFieldCount =
                    fillResult.SkippedFieldCount,

                ActionText =
                    nextAction.ActionText,

                NavigationAttempted = false,
                Navigated = false,

                RequiresManualInteraction = false,

                Message =
                    "The current state is filled and a safe Next action is ready. No button has been clicked yet."
            };
        }
        finally
        {
            session.OperationLock.Release();
        }
    }

    public async Task<AuthenticatedAuditFieldFillResult>
    FillCurrentStateAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(
            sessionId,
            out AuthenticatedAuditBrowserSession? session))
        {
            throw new KeyNotFoundException(
                "The authenticated audit session was not found.");
        }

        await session.OperationLock.WaitAsync(cancellationToken);

        try
        {
            if (session.IsStopping ||
                !_sessions.ContainsKey(sessionId))
            {
                throw new KeyNotFoundException(
                    "The authenticated audit session is no longer active.");
            }

            if (!session.Browser.IsConnected)
            {
                throw new InvalidOperationException(
                    "The authenticated browser is no longer connected.");
            }

            IPage activePage =
                SelectPageForAudit(session);

            session.ActivePage = activePage;

            await activePage.BringToFrontAsync();

            return await FillSafeFieldsAsync(activePage);
        }
        finally
        {
            session.OperationLock.Release();
        }
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

    private static string BuildElementFixGuidance(
    string ruleId)
    {
        string normalizedRuleId =
            ruleId.Trim().ToLowerInvariant();

        string guidance =
            normalizedRuleId switch
            {
                "label" =>
                    "Add a visible <label> for this form control. " +
                    "The label's for attribute must match the control's id. " +
                    "When a visible label is not appropriate, provide an accessible name using aria-label or aria-labelledby.",

                "button-name" =>
                    "Give this button an accessible name using visible text, " +
                    "aria-label, or aria-labelledby. The name should clearly describe the button's action.",

                "link-name" =>
                    "Add meaningful visible link text or provide an accessible name using aria-label or aria-labelledby. " +
                    "The accessible name should describe the link's destination or purpose.",

                "image-alt" =>
                    "Add an alt attribute that describes the image's purpose. " +
                    "Use alt=\"\" only when the image is decorative and should be ignored by assistive technology.",

                "input-image-alt" =>
                    "Add an alt attribute that describes the action performed by this image input.",

                "select-name" =>
                    "Associate this select control with a visible <label>, or provide an accessible name using " +
                    "aria-label or aria-labelledby.",

                "color-contrast" =>
                    "Change this element's foreground or background color until it meets the required WCAG contrast ratio. " +
                    "Normal text generally requires 4.5:1, while large text generally requires 3:1.",

                "aria-valid-attr-value" =>
                    "Correct or remove the invalid ARIA attribute value. " +
                    "When the value references another element ID, confirm that the referenced element exists on the page.",

                "aria-allowed-attr" =>
                    "Remove the ARIA attribute that is not allowed for this element or change the element's role " +
                    "to one that supports the attribute.",

                "aria-required-attr" =>
                    "Add the ARIA attribute required by this element's role and give it an appropriate value.",

                "aria-required-children" =>
                    "Add the required child roles inside this element, or change the parent role so that it matches " +
                    "the element's actual structure.",

                "aria-required-parent" =>
                    "Place this element inside a parent with the required ARIA role, or change the element's role " +
                    "to match its actual structure.",

                "frame-title" =>
                    "Add a concise and meaningful title attribute to this frame describing the content or purpose of the embedded page.",

                "duplicate-id-aria" =>
                    "Change this element's id so that every referenced ID on the page is unique. " +
                    "Update any labels or ARIA attributes that reference the old ID.",

                "nested-interactive" =>
                    "Remove the interactive control nested inside this element. " +
                    "Use one interactive element, or separate the controls so each can receive focus independently.",

                "heading-order" =>
                    "Change this heading level so the page follows a logical hierarchy without skipping heading levels.",

                "html-has-lang" =>
                    "Add a valid lang attribute to the page's <html> element, such as lang=\"en\".",

                "document-title" =>
                    "Add a meaningful <title> element inside the page's <head> that identifies the page or current workflow step.",

                "landmark-one-main" =>
                    "Place the page's primary content inside one <main> element or an element with role=\"main\". " +
                    "Only one main landmark should be present.",

                "region" =>
                    "Place this content inside an appropriate landmark such as <main>, <nav>, <header>, <footer>, or an explicitly labeled region.",

                _ =>
                    "Review this element using the axe-core failure explanation below. " +
                    "Update the element's HTML, accessible name, role, state, or relationship so the stated requirement is satisfied."
            };

        return guidance;
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

    private static async Task<AuthenticatedAuditNavigationAnalysisResult>
    AnalyzePageForAutomaticNavigationAsync(IPage page)
    {
        /*
         * This only inspects the rendered page. It does not fill fields,
         * click buttons, submit forms, or change the manual scan workflow.
         */
        return await page.EvaluateAsync<
            AuthenticatedAuditNavigationAnalysisResult>(
            """
        () => {
            const isVisibleAndEnabled = element => {
                if (!(element instanceof HTMLElement)) {
                    return false;
                }

                const style = window.getComputedStyle(element);
                const rectangle = element.getBoundingClientRect();

                return style.display !== "none" &&
                    style.visibility !== "hidden" &&
                    rectangle.width > 0 &&
                    rectangle.height > 0 &&
                    !element.hasAttribute("disabled") &&
                    element.getAttribute("aria-disabled") !== "true";
            };

            function getActionText(element) {
                return [
                    element.innerText,
                    element.textContent,
                    element.getAttribute("aria-label"),
                    element.getAttribute("title"),
                    element.getAttribute("name"),
                    element.getAttribute("value")
                ]
                    .filter(value => value)
                    .join(" ")
                    .replace(/\s+/g, " ")
                    .trim();
            }

            const normalizeText = value =>
                (value ?? "")
                    .replace(/\s+/g, " ")
                    .trim();

            const isClaimsRefundHub =
                window.location.pathname
                    .toLowerCase()
                    .includes(
                        "/refunds/claims/home/newclaim") &&
                /application requirements/i.test(
                    normalizeText(
                        document.body?.innerText));

            const visibleForms =
                Array.from(document.querySelectorAll("form"))
                    .filter(isVisibleAndEnabled);

            const requiredFields =
                Array.from(
                    document.querySelectorAll(
                        "input[required], select[required], textarea[required], " +
                        "[aria-required='true']"))
                    .filter(isVisibleAndEnabled);

            const visibleFormControls =
                Array.from(
                    document.querySelectorAll(
                        "input:not([type='hidden']):not([type='button'])" +
                        ":not([type='submit']):not([type='reset'])" +
                        ":not([type='image']), select, textarea"))
                    .filter(isVisibleAndEnabled);

            const hasVisibleForm =
                visibleForms.length > 0;

            const possibleActions =
                Array.from(
                    document.querySelectorAll(
                        "button, input[type='button'], " +
                        "input[type='submit'], a[href], " +
                        "[role='button']"))
                    .filter(isVisibleAndEnabled);

            const getWorkflowHub = () => {
            const unfinishedPattern =
                /\b(not completed|incomplete|not started|pending|optional|required|needs attention)\b/i;

            const notCompletedPattern =
                /\b(not completed|incomplete|not started|pending|needs attention)\b/i;

            const optionalPattern =
                /\boptional\b/i;

            const requiredPattern =
                /\brequired\b/i;

            const completedPattern =
                /\b(completed|complete|done)\b/i;

            const excludedPattern =
                /\b(n\/a|not applicable)\b/i;

            const unsafePattern =
                /\b(submit|finalize|certify|pay|payment|purchase|checkout|place order|logout|sign out)\b/i;

            const findNearestStatusContainer =
                action => {
                    let current =
                        action.parentElement;

                    for (
                        let depth = 0;
                        current && depth < 7;
                        depth++) {

                        if (
                            current === document.body ||
                            current ===
                                document.documentElement) {
                            break;
                        }

                        const text =
                            normalizeText(
                                current.innerText);

                        if (
                            text.length > 0 &&
                            text.length <= 1000 &&
                            (
                                unfinishedPattern.test(text) ||
                                completedPattern.test(text) ||
                                excludedPattern.test(text)
                            )) {
                            return current;
                        }

                        current =
                            current.parentElement;
                    }

                    return null;
                };

            const groupedCandidates =
                new Map();

            for (const action of possibleActions) {
                const container =
                    findNearestStatusContainer(
                        action);

                if (!container) {
                    continue;
                }

                const containerText =
                    normalizeText(
                        container.innerText);

                const actionText =
                    normalizeText(
                        getActionText(action));

                const isNotCompleted =
                    notCompletedPattern.test(
                        containerText);

                const isOptional =
                    optionalPattern.test(
                        containerText);

                const isRequired =
                    requiredPattern.test(
                        containerText);

                const isCompletedOnly =
                    completedPattern.test(
                        containerText) &&
                    !isNotCompleted;

                const isExcluded =
                    excludedPattern.test(
                        containerText);

                const isUnsafe =
                    unsafePattern.test(
                        actionText) ||
                    /\bsubmit application\b/i.test(
                        containerText);

                let score = 0;

                if (actionText.length > 0) {
                    score += 3;
                }

                if (
                    action instanceof
                        HTMLAnchorElement) {
                    score += 2;
                }

                if (
                    action instanceof
                        HTMLButtonElement) {
                    score += 1;
                }

                const existing =
                    groupedCandidates.get(
                        container);

                if (
                    !existing ||
                    score > existing.Score) {
                    groupedCandidates.set(
                        container,
                        {
                            Element: action,
                            ContainerText:
                                containerText,
                            IsNotCompleted:
                                isNotCompleted,
                            IsOptional:
                                isOptional,
                            IsRequired:
                                isRequired,
                            IsCompletedOnly:
                                isCompletedOnly,
                            IsExcluded:
                                isExcluded,
                            IsUnsafe:
                                isUnsafe,
                            Score: score
                        });
                }
            }

            const rows =
                Array.from(
                    groupedCandidates.values());

            /*
             * Multiple status-based sections and no visible
             * form controls strongly indicate a workflow hub.
             */
            const isHub =
                visibleFormControls.length === 0 &&
                rows.length >= 2;

            const candidates =
                rows.filter(candidate =>
                    !candidate.IsCompletedOnly &&
                    !candidate.IsExcluded &&
                    !candidate.IsUnsafe &&
                    (
                        candidate.IsNotCompleted ||
                        candidate.IsOptional ||
                        candidate.IsRequired
                    ));

            return {
                IsHub: isHub,
                Candidates: candidates
            };
            };

            const workflowHub =
            getWorkflowHub();

            const hasWorkflowControls =
            visibleFormControls.length > 0 ||
            requiredFields.length > 0 ||
            workflowHub.IsHub ||
            isClaimsRefundHub;

            const findClaimsRefundHubAction =
                row => {
                    const rowText =
                        normalizeText(row.innerText);

                    if (
                        /submit application/i.test(
                            rowText) ||
                        /\battachments\b/i.test(
                            rowText)) {
                        return null;
                    }

                    const rowActions =
                        Array.from(
                            row.querySelectorAll(
                                "button, " +
                                "input[type='button'], " +
                                "input[type='submit'], " +
                                "a[href], " +
                                "[role='button']"))
                            .filter(
                                isVisibleAndEnabled);

                    /*
                    * The first row should only be selected while
                    * its actual Start control is available.
                    */
                    if (
                        /start a new claim/i.test(
                            rowText)) {
                        return rowActions.find(
                            action =>
                                /\bstart\b/i.test(
                                    getActionText(action))) ??
                            null;
                    }

                    if (
                        !/not completed/i.test(
                            rowText)) {
                        return null;
                    }

                    return (
                        rowActions.find(
                            action =>
                                action instanceof
                                    HTMLButtonElement) ??
                        rowActions.find(
                            action =>
                                action instanceof
                                    HTMLInputElement) ??
                        rowActions.find(
                            action =>
                                action instanceof
                                HTMLAnchorElement) ??
                        rowActions[0] ??
                        null
                    );
                };

            const claimsRefundHubActions =
                isClaimsRefundHub
                    ? Array.from(
                        document.querySelectorAll("tr"))
                        .map(findClaimsRefundHubAction)
                        .filter(action => action !== null)
                    : [];

                        /*
                        * Include required and optional sections.
                        * Completed rows will no longer match.
                        */
                        if (
                            !/\b(not completed|optional)\b/i.test(
                                rowText)) {
                            return null;
                        }

                        const actions =
                            Array.from(
                                row.querySelectorAll(
                                    "a[href], " +
                                    "button, " +
                                    "input[type='button'], " +
                                    "input[type='submit'], " +
                                    "[role='button']"))
                                .filter(
                                    isVisibleAndEnabled);

                        return actions.find(
                            action => {
                                const text =
                                    getActionText(action);

                                return text &&
                                    !/\b(submit|logout)\b/i.test(
                                        text);
                            }) ?? null;
                    })
                    .filter(
                        action =>
                            action !== null)
                    : [];

            const safeNextPattern =
                /\b(next|continue|proceed|start|begin|advance)\b/i;

            const unsafeActionPattern =
                /\b(submit|finalize|certify|pay|payment|purchase|checkout|place order|sign|signature|send application)\b/i;

            const normalNextActions =
            possibleActions.filter(element => {
                const actionText =
                    getActionText(element);

                const isFormAssociatedAction =
                    element.closest("form") !== null ||
                    (
                        element instanceof
                            HTMLButtonElement &&
                        element.form !== null
                    ) ||
                    (
                        element instanceof
                            HTMLInputElement &&
                        element.form !== null
                    );

                return hasWorkflowControls &&
                    safeNextPattern.test(
                        actionText) &&
                    !unsafeActionPattern.test(
                        actionText) &&
                    (
                        !hasVisibleForm ||
                        isFormAssociatedAction
                    );
            });

            const hubStartAction =
            workflowHub.IsHub
                ? possibleActions.find(element =>
                    /\b(start|begin)\b/i.test(
                        getActionText(element)) &&
                    !unsafeActionPattern.test(
                        getActionText(element)))
                : null;

            let candidateNextActions = [];

            if (isClaimsRefundHub) {
            candidateNextActions =
                claimsRefundHubActions;
            }
            else if (workflowHub.IsHub) {
            const hubStartAction =
                possibleActions.find(element => {
                    const text =
                        getActionText(element);

                    return /\b(start|begin)\b/i.test(text) &&
                        !unsafeActionPattern.test(text);
                });

            if (hubStartAction) {
                candidateNextActions =
                    [hubStartAction];
            }
            else {
                candidateNextActions =
                    workflowHub.Candidates.map(
                        candidate =>
                            candidate.Element);
            }
            }
            else {
            candidateNextActions =
                possibleActions.filter(element => {
                    const actionText =
                        getActionText(element);

                    const isFormAssociatedAction =
                        element.closest("form") !== null ||
                        (
                            element instanceof
                                HTMLButtonElement &&
                            element.form !== null
                        ) ||
                        (
                            element instanceof
                                HTMLInputElement &&
                            element.form !== null
                        );

                    return hasWorkflowControls &&
                        safeNextPattern.test(
                            actionText) &&
                        !unsafeActionPattern.test(
                            actionText) &&
                        (
                            !hasVisibleForm ||
                            isFormAssociatedAction
                        );
                });
            }

            const hasCaptcha =
                document.querySelector(
                    "iframe[src*='recaptcha'], " +
                    "iframe[src*='hcaptcha'], " +
                    "[id*='captcha' i], " +
                    "[class*='captcha' i], " +
                    "input[name*='captcha' i]") !== null;

            const hasFileUpload =
                Array.from(
                    document.querySelectorAll("input[type='file']"))
                    .some(isVisibleAndEnabled);

            const hasPasswordField =
                Array.from(
                    document.querySelectorAll("input[type='password']"))
                    .some(isVisibleAndEnabled);

            const hasPaymentField =
                document.querySelector(
                    "[autocomplete='cc-number'], " +
                    "[autocomplete='cc-csc'], " +
                    "[autocomplete='cc-exp'], " +
                    "input[name*='cardnumber' i], " +
                    "input[name*='creditcard' i]") !== null;

            let stopReason = null;

            if (hasCaptcha) {
                stopReason =
                    "CAPTCHA detected. Manual interaction is required.";
            }
            else if (hasFileUpload) {
                stopReason =
                    "File upload detected. Manual interaction is required.";
            }
            else if (hasPaymentField) {
                stopReason =
                    "Payment fields detected. Automatic navigation is disabled.";
            }
            else if (hasPasswordField) {
                stopReason =
                    "Authentication fields detected. Sign in manually first.";
            }

            let pageType = "StaticPage";

            if (stopReason) {
                pageType = "Unsupported";
            }
            else if (
                hasWorkflowControls &&
                candidateNextActions.length > 0) {
                pageType = "NavigableWorkflow";
            }
            else if (
                hasVisibleForm ||
                hasWorkflowControls) {
                pageType = "Form";
            }

            let recommendedAction;

            if (stopReason) {
                recommendedAction = "Continue manually.";
            }
            else if (candidateNextActions.length > 0) {
                recommendedAction =
                    "The page may support automatic navigation.";
            }
            else if (
                visibleForms.length > 0 ||
                requiredFields.length > 0) {
                recommendedAction =
                    "Form detected, but no safe Next action was found.";
            }
            else {
                recommendedAction =
                    "Scan this page once and stop normally.";
            }

            return {
                PageType: pageType,
                FormCount: visibleForms.length,
                RequiredFieldCount: requiredFields.length,
                CandidateNextActionCount:
                    candidateNextActions.length,
                CanNavigateAutomatically:
                    stopReason === null &&
                    hasWorkflowControls &&
                    candidateNextActions.length > 0,
                RecommendedAction: recommendedAction,
                StopReason: stopReason
            };
        }
        """);
    }

    private static async Task<AuthenticatedAuditNextActionResult>
    FindSafeNextActionAsync(IPage page)
    {
        string resultJson =
            await page.EvaluateAsync<string>(
                """
            () => JSON.stringify((() => {
                const markerAttribute =
                    "data-city-audit-next-action";

                /*
                 * Remove an old marker in case this page was analyzed
                 * previously.
                 */
                document
                    .querySelectorAll(
                        `[${markerAttribute}]`)
                    .forEach(element =>
                        element.removeAttribute(
                            markerAttribute));

                const isVisibleAndEnabled = element => {
                    if (!(element instanceof HTMLElement)) {
                        return false;
                    }

                    const style =
                        window.getComputedStyle(element);

                    const rectangle =
                        element.getBoundingClientRect();

                    return style.display !== "none" &&
                        style.visibility !== "hidden" &&
                        rectangle.width > 0 &&
                        rectangle.height > 0 &&
                        !element.hasAttribute("disabled") &&
                        element.getAttribute("aria-disabled") !==
                            "true";
                };

                /*
                * A visible final-action button means the workflow has reached a
                * review or submission page. Stop before considering navigation
                * links such as "Start" in the progress bar.
                */
                const isFinalActionVisible = element => {
                    if (!(element instanceof HTMLElement)) {
                        return false;
                    }

                    const style =
                        window.getComputedStyle(element);

                    const rectangle =
                        element.getBoundingClientRect();

                    return (
                        style.display !== "none" &&
                        style.visibility !== "hidden" &&
                        rectangle.width > 0 &&
                        rectangle.height > 0
                    );
                };

                const normalizeText = value =>
                    (value ?? "")
                        .replace(/\s+/g, " ")
                        .trim();

                const isClaimsRefundHub =
                    window.location.pathname
                        .toLowerCase()
                        .includes(
                            "/refunds/claims/home/newclaim") &&
                    /application requirements/i.test(
                        normalizeText(
                            document.body?.innerText));

                const uploadAction =
                    isRPermitPage
                        ? Array.from(
                            document.querySelectorAll(
                                "button, " +
                                "input[type='button'], " +
                                "input[type='submit'], " +
                                "a[href], " +
                                "[role='button']"))
                            .filter(isVisibleAndEnabled)
                            .find(element =>
                                /\bupload\b/i.test(
                                    getActionText(element)))
                        : null;

                if (uploadAction) {
                    uploadAction.setAttribute(
                        markerAttribute,
                        "true");

                    return {
                        Found: true,
                        ActionText:
                            getActionText(uploadAction) ||
                            "Upload",
                        Selector:
                            `[${markerAttribute}="true"]`,
                        CandidateCount: 1,
                        RequiresManualInteraction: false,
                        StopReason: null
                    };
                }             

                if (isClaimsRefundHub) {
                    const hubCandidates =
                        Array.from(
                            document.querySelectorAll("tr"))
                            .map(row => {
                                if (
                                    !isVisibleAndEnabled(row)) {
                                    return null;
                                }

                                const rowText =
                                    normalizeText(
                                        row.innerText);

                                if (
                                    /submit application/i.test(
                                        rowText) ||
                                    /\battachments\b/i.test(
                                        rowText)) {
                                    return null;
                                }

                                const rowActions =
                                    Array.from(
                                        row.querySelectorAll(
                                            "button, " +
                                            "input[type='button'], " +
                                            "input[type='submit'], " +
                                            "a[href], " +
                                            "[role='button']"))
                                        .filter(
                                            isVisibleAndEnabled);

                                let selectedAction = null;

                                if (
                                    /start a new claim/i.test(
                                        rowText)) {
                                    selectedAction =
                                        rowActions.find(
                                            action =>
                                                /\bstart\b/i.test(
                                                    getActionText(
                                                        action))) ??
                                        null;
                                }
                                else if (
                                    /not completed/i.test(
                                        rowText)) {
                                    selectedAction =
                                        rowActions.find(
                                            action =>
                                                action instanceof
                                                    HTMLButtonElement) ??
                                        rowActions.find(
                                            action =>
                                                action instanceof
                                                    HTMLInputElement) ??
                                        rowActions.find(
                                            action =>
                                                action instanceof
                                                    HTMLAnchorElement) ??
                                        rowActions[0] ??
                                        null;
                                }

                                return selectedAction
                                    ? {
                                        Element:
                                            selectedAction,
                                        RowText:
                                            rowText
                                    }
                                    : null;
                            })
                            .filter(
                                candidate =>
                                    candidate !== null);
                                    
                    if (hubCandidates.length > 0) {
                        const selected =
                            hubCandidates[0];

                        selected.Element.setAttribute(
                            markerAttribute,
                            "true");

                        return {
                            Found: true,
                            ActionText:
                                selected.RowText,
                            Selector:
                                `[${markerAttribute}="true"]`,
                            CandidateCount:
                                hubCandidates.length,
                            RequiresManualInteraction:
                                false,
                            StopReason: null
                        };
                    }
                }

                const finalActionPattern =
                    /\b(submit|finalize|certify|pay|payment|purchase|checkout|place order|complete application|finish application)\b/i;

                const finalAction =
                    Array.from(
                        document.querySelectorAll(
                            [
                                "button",
                                "input[type='submit']",
                                "input[type='button']",
                                "[role='button']"
                            ].join(",")
                        )
                    )
                    .find(element => {
                        if (!isFinalActionVisible(element)) {
                            return false;
                        }

                        const actionText =
                            (
                                element.textContent ||
                                element.value ||
                                element.getAttribute("aria-label") ||
                                ""
                            )
                                .replace(/\s+/g, " ")
                                .trim();

                        return finalActionPattern.test(
                            actionText);
                    });

                if (finalAction) {
                    const finalActionText =
                        (
                            finalAction.textContent ||
                            finalAction.value ||
                            finalAction.getAttribute("aria-label") ||
                            "Final action"
                        )
                            .replace(/\s+/g, " ")
                            .trim();

                    return {
                        Found: false,
                        ActionText: finalActionText,
                        Selector: null,
                        CandidateCount: 0,
                        RequiresManualInteraction: true,
                        StopReason:
                            `The workflow reached the final "${finalActionText}" page. Submission was left for manual review.`
                    };
                }

                function getActionText(element) {
                    return [
                        element.innerText,
                        element.textContent,
                        element.getAttribute("aria-label"),
                        element.getAttribute("title"),
                        element.getAttribute("value"),
                        element.getAttribute("name")
                    ]
                        .filter(value => value)
                        .join(" ")
                        .replace(/\s+/g, " ")
                        .trim();
                }

                const safePattern =
                    /\b(next|continue|proceed|start|begin|advance|save)\b/i;

                const unsafePattern =
                    /\b(submit|finalize|certify|pay|payment|purchase|checkout|place order|sign|signature|send application|complete application|finish)\b/i;

                const actions =
                    Array.from(
                        document.querySelectorAll(
                            "button, " +
                            "input[type='button'], " +
                            "input[type='submit'], " +
                            "a[href], " +
                            "[role='button']"))
                        .filter(isVisibleAndEnabled);

                const getWorkflowHub = () => {
                const unfinishedPattern =
                    /\b(not completed|incomplete|not started|pending|optional|required|needs attention)\b/i;

                const notCompletedPattern =
                    /\b(not completed|incomplete|not started|pending|needs attention)\b/i;

                const optionalPattern =
                    /\boptional\b/i;

                const requiredPattern =
                    /\brequired\b/i;

                const completedPattern =
                    /\b(completed|complete|done)\b/i;

                const excludedPattern =
                    /\b(n\/a|not applicable)\b/i;

                const findNearestStatusContainer =
                    action => {
                        let current =
                            action.parentElement;

                        for (
                            let depth = 0;
                            current && depth < 7;
                            depth++) {

                            if (
                                current === document.body ||
                                current ===
                                    document.documentElement) {
                                break;
                            }

                            const text =
                                normalizeText(
                                    current.innerText);

                            if (
                                text.length > 0 &&
                                text.length <= 1000 &&
                                (
                                    unfinishedPattern.test(text) ||
                                    completedPattern.test(text) ||
                                    excludedPattern.test(text)
                                )) {
                                return current;
                            }

                            current =
                                current.parentElement;
                        }

                        return null;
                    };

                const groupedCandidates =
                    new Map();

                for (const action of actions) {
                    const container =
                        findNearestStatusContainer(
                            action);

                    if (!container) {
                        continue;
                    }

                    const containerText =
                        normalizeText(
                            container.innerText);

                    const actionText =
                        normalizeText(
                            getActionText(action));

                    const isNotCompleted =
                        notCompletedPattern.test(
                            containerText);

                    const isOptional =
                        optionalPattern.test(
                            containerText);

                    const isRequired =
                        requiredPattern.test(
                            containerText);

                    const isCompletedOnly =
                        completedPattern.test(
                            containerText) &&
                        !isNotCompleted;

                    const isExcluded =
                        excludedPattern.test(
                            containerText);

                    const isUnsafe =
                        unsafePattern.test(
                            actionText) ||
                        /\bsubmit application\b/i.test(
                            containerText);

                    let score = 0;

                    if (actionText.length > 0) {
                        score += 3;
                    }

                    if (
                        action instanceof
                            HTMLAnchorElement) {
                        score += 2;
                    }

                    if (
                        action instanceof
                            HTMLButtonElement) {
                        score += 1;
                    }

                    const existing =
                        groupedCandidates.get(
                            container);

                    if (
                        !existing ||
                        score > existing.Score) {
                        groupedCandidates.set(
                            container,
                            {
                                Element: action,
                                ContainerText:
                                    containerText,
                                IsNotCompleted:
                                    isNotCompleted,
                                IsOptional:
                                    isOptional,
                                IsRequired:
                                    isRequired,
                                IsCompletedOnly:
                                    isCompletedOnly,
                                IsExcluded:
                                    isExcluded,
                                IsUnsafe:
                                    isUnsafe,
                                Score: score
                            });
                    }
                }

                const rows =
                    Array.from(
                        groupedCandidates.values());

                const visibleFormControlCount =
                    Array.from(
                        document.querySelectorAll(
                            "input:not([type='hidden']), " +
                            "select, textarea"))
                        .filter(
                            isVisibleAndEnabled)
                        .length;

                return {
                    IsHub:
                        visibleFormControlCount === 0 &&
                        rows.length >= 2,

                    Candidates:
                        rows.filter(candidate =>
                            !candidate.IsCompletedOnly &&
                            !candidate.IsExcluded &&
                            !candidate.IsUnsafe &&
                            (
                                candidate.IsNotCompleted ||
                                candidate.IsOptional ||
                                candidate.IsRequired
                            ))
                };
            };

            const workflowHub =
                getWorkflowHub();

            if (workflowHub.IsHub) {
                /*
                 * A visible Start or Begin button takes priority
                 * over the individual section links.
                 */
                const startAction =
                    actions.find(action =>
                        /\b(start|begin)\b/i.test(
                            getActionText(action)) &&
                        !unsafePattern.test(
                            getActionText(action)));

                const visitedKey =
                    `city-audit-hub-visited:` +
                    `${window.location.origin}` +
                    `${window.location.pathname}` +
                    `${window.location.search}`;

                let visitedOptionalActions = [];

                try {
                    visitedOptionalActions =
                        JSON.parse(
                            sessionStorage.getItem(
                                visitedKey) ?? "[]");
                }
                catch {
                    visitedOptionalActions = [];
                }

                const getCandidateKey =
                    candidate => {
                        const element =
                            candidate.Element;

                        let destination = "";

                        if (
                            element instanceof
                                HTMLAnchorElement) {
                            destination =
                                element.href;
                        }

                        return (
                            destination +
                            "|" +
                            normalizeText(
                                candidate.ContainerText)
                        );
                    };

                const selectedCandidate =
                    workflowHub.Candidates.find(
                        candidate => {
                            if (!candidate.IsOptional) {
                                return true;
                            }

                            return !visitedOptionalActions
                                .includes(
                                    getCandidateKey(
                                        candidate));
                        });

                if (
                    startAction ||
                    selectedCandidate) {

                    const selectedElement =
                        startAction ??
                        selectedCandidate.Element;

                    const selectedText =
                        startAction
                            ? getActionText(
                                startAction)
                            : selectedCandidate
                                .ContainerText;

                    if (
                        !startAction &&
                        selectedCandidate.IsOptional) {

                        visitedOptionalActions.push(
                            getCandidateKey(
                                selectedCandidate));

                        sessionStorage.setItem(
                            visitedKey,
                            JSON.stringify(
                                visitedOptionalActions));
                    }

                    selectedElement.setAttribute(
                        markerAttribute,
                        "true");

                    return {
                        Found: true,
                        ActionText:
                            selectedText ||
                            "Workflow section",
                        Selector:
                            `[${markerAttribute}="true"]`,
                        CandidateCount:
                            workflowHub.Candidates.length,
                        RequiresManualInteraction:
                            false,
                        StopReason: null
                    };
                }
                }
                
                const unsafeVisibleAction =
                    actions.find(element =>
                        unsafePattern.test(
                            getActionText(element)));

                const candidates =
                    actions.filter(element => {
                        const text =
                            getActionText(element);

                        return safePattern.test(text) &&
                            !unsafePattern.test(text);
                    });

                if (candidates.length === 0) {
                    return {
                        Found: false,
                        ActionText: null,
                        Selector: null,
                        CandidateCount: 0,
                        RequiresManualInteraction:
                            unsafeVisibleAction !== undefined,
                        StopReason:
                            unsafeVisibleAction
                                ? "Only a final or potentially irreversible action was detected."
                                : "No safe Next or Continue action was detected."
                    };
                }

                /*
                 * Prefer an actual button before a link or generic
                 * role=button element.
                 */
                const selected =
                    candidates.find(element =>
                element instanceof HTMLButtonElement) ??
                    candidates.find(element =>
                element instanceof HTMLInputElement) ??
                    candidates[0];

                selected.setAttribute(
                    markerAttribute,
                    "true");

                return {
                    Found: true,
                    ActionText:
                        getActionText(selected) ||
                        "Next action",
                    Selector:
                        `[${markerAttribute}="true"]`,
                    CandidateCount:
                        candidates.length,
                    RequiresManualInteraction: false,
                    StopReason: null
                };
            })())
            """);

        AuthenticatedAuditNextActionResult? result =
            System.Text.Json.JsonSerializer.Deserialize<
                AuthenticatedAuditNextActionResult>(
                    resultJson);

        return result ??
            throw new InvalidOperationException(
                "The next-action analysis result could not be read.");
    }

    private static async Task<string>
    GetAutomaticNavigationStateSignatureAsync(IPage page)
    {
        /*
         * Creates a lightweight description of the rendered state.
         * It lets automatic navigation detect a new step even when the
         * application's URL does not change.
         */
        return await page.EvaluateAsync<string>(
            """
        () => {
            const normalize = value =>
                (value ?? "")
                    .replace(/\s+/g, " ")
                    .trim();

            const headings =
                Array.from(
                    document.querySelectorAll(
                        "h1, h2, [role='heading']"))
                    .map(element =>
                        normalize(element.textContent))
                    .filter(Boolean)
                    .slice(0, 20);

            const fields =
                Array.from(
                    document.querySelectorAll(
                        "input:not([type='hidden']), " +
                        "select, textarea"))
                    .map(element => ({
                        id: element.id ?? "",
                        name:
                            element.getAttribute("name") ?? "",
                        type:
                            element.getAttribute("type") ??
                            element.tagName,
                        label:
                            normalize(
                                element.getAttribute(
                                    "aria-label"))
                    }))
                    .slice(0, 100);

            const actions =
                Array.from(
                    document.querySelectorAll(
                        "button, input[type='submit'], " +
                        "input[type='button'], [role='button']"))
                    .map(element =>
                        normalize(
                            element.innerText ||
                            element.textContent ||
                            element.getAttribute("value") ||
                            element.getAttribute("aria-label")))
                    .filter(Boolean)
                    .slice(0, 50);

            return JSON.stringify({
                url: window.location.href,
                title: document.title,
                headings,
                fields,
                actions
            });
        }
        """);
    }

    private static async Task<AuthenticatedAuditFieldFillResult>
    FillSafeFieldsAsync(IPage page)
    {
        /*
         * Fill ordinary rendered form controls with harmless test values.
         * This method never clicks buttons or submits the form.
         */
        string resultJson =
            await page.EvaluateAsync<string>(
        """
        () => JSON.stringify((() => {
            const filledDescriptions = [];
            const skippedDescriptions = [];

            const isVisibleAndEnabled = element => {
                if (!(element instanceof HTMLElement)) {
                    return false;
                }

                const style =
                    window.getComputedStyle(element);

                const rectangle =
                    element.getBoundingClientRect();

                return style.display !== "none" &&
                    style.visibility !== "hidden" &&
                    rectangle.width > 0 &&
                    rectangle.height > 0 &&
                    !element.hasAttribute("disabled") &&
                    element.getAttribute("aria-disabled") !== "true";
            };

            const getDescription = element => {
                const id = element.id?.trim();

                let labelText = "";

                if (id) {
                    const explicitLabel =
                        document.querySelector(
                            `label[for="${CSS.escape(id)}"]`);

                    labelText =
                        explicitLabel?.textContent?.trim() ?? "";
                }

                if (!labelText) {
                    labelText =
                        element.closest("label")
                            ?.textContent
                            ?.trim() ?? "";
                }

                return (
                    labelText ||
                    element.getAttribute("aria-label") ||
                    element.getAttribute("name") ||
                    element.getAttribute("placeholder") ||
                    element.id ||
                    element.getAttribute("type") ||
                    element.tagName
                )
                    .replace(/\s+/g, " ")
                    .trim();
            };

            const getFieldHint = element => {
                return [
                    getDescription(element),
                    element.getAttribute("name"),
                    element.id,
                    element.getAttribute("autocomplete"),
                    element.getAttribute("placeholder")
                ]
                    .filter(value => value)
                    .join(" ")
                    .toLowerCase();
            };

            const setNativeValue = (element, value) => {
                let prototype;

                if (element instanceof HTMLSelectElement) {
                    prototype =
                        HTMLSelectElement.prototype;
                }
                else if (
                    element instanceof HTMLTextAreaElement) {
                    prototype =
                        HTMLTextAreaElement.prototype;
                }
                else {
                    prototype =
                        HTMLInputElement.prototype;
                }

                const valueSetter =
                    Object.getOwnPropertyDescriptor(
                        prototype,
                        "value")
                    ?.set;

                if (valueSetter) {
                    valueSetter.call(element, value);
                }
                else {
                    element.value = value;
                }

                element.dispatchEvent(
                    new Event(
                        "input",
                        {
                            bubbles: true
                        }));

                element.dispatchEvent(
                    new Event(
                        "change",
                        {
                            bubbles: true
                        }));
            };

            const setNativeChecked = (
                element,
                checked) => {
                const checkedSetter =
                    Object.getOwnPropertyDescriptor(
                        HTMLInputElement.prototype,
                        "checked")
                    ?.set;

                if (checkedSetter) {
                    checkedSetter.call(
                        element,
                        checked);
                }
                else {
                    element.checked = checked;
                }

                element.dispatchEvent(
                    new Event(
                        "input",
                        {
                            bubbles: true
                        }));

                element.dispatchEvent(
                    new Event(
                        "change",
                        {
                            bubbles: true
                        }));
            };

            const visibleElements =
                elements =>
                    Array.from(elements)
                        .filter(isVisibleAndEnabled);

            const captchaDetected =
                document.querySelector(
                    "iframe[src*='recaptcha'], " +
                    "iframe[src*='hcaptcha'], " +
                    "[id*='captcha' i], " +
                    "[class*='captcha' i], " +
                    "input[name*='captcha' i]") !== null;

            if (captchaDetected) {
                return {
                    FilledFieldCount: 0,
                    SkippedFieldCount: 0,
                    RequiresManualInteraction: true,
                    StopReason:
                        "CAPTCHA detected. Manual interaction is required.",
                    FilledFieldDescriptions: [],
                    SkippedFieldDescriptions: []
                };
            }

            const unsafeControls =
                visibleElements(
                    document.querySelectorAll(
                    "input[type='password'], " +
                    "input[type='file'], " +
                    "input[name*='routing' i], " +
                    "input[name*='bankaccount' i], " +
                    "input[name*='socialsecurity' i], " +
                    "input[name*='ssn' i], " +
                    "[name*='signature' i], " +
                    "[id*='signature' i], " +
                    "[aria-label*='signature' i]"));

            if (unsafeControls.length > 0) {
                return {
                    FilledFieldCount: 0,
                    SkippedFieldCount:
                        unsafeControls.length,
                    RequiresManualInteraction: true,
                    StopReason:
                        "A password, file upload, banking, sensitive-information, or signature field was detected.",
                    FilledFieldDescriptions: [],
                    SkippedFieldDescriptions:
                        unsafeControls.map(
                            getDescription)
                };
            }

            /*
             * Avoid automatically accepting certifications,
             * legal agreements, or consent statements.
             */
            const unsafeAgreementPattern =
                /\b(certify|attest|signature|authorize|consent|agree|terms and conditions)\b/i;

            const agreementControl =
                visibleElements(
                    document.querySelectorAll(
                        "input[type='checkbox']"))
                    .find(element =>
                        unsafeAgreementPattern.test(
                            getDescription(element)));

            if (agreementControl) {
                return {
                    FilledFieldCount: 0,
                    SkippedFieldCount: 1,
                    RequiresManualInteraction: true,
                    StopReason:
                        "A certification, consent, or agreement checkbox requires manual review.",
                    FilledFieldDescriptions: [],
                    SkippedFieldDescriptions:
                    [
                        getDescription(
                            agreementControl)
                    ]
                };
            }

            const controlSet =
                new Set([
                    ...document.querySelectorAll(
                        "form input, " +
                        "form select, " +
                        "form textarea"),
                    ...document.querySelectorAll(
                        "input[required], " +
                        "select[required], " +
                        "textarea[required], " +
                        "[aria-required='true']")
                ]);

            const controls =
                Array.from(controlSet);

            const getTextValue = element => {
                const hint =
                    getFieldHint(element);

                if (
                    hint.includes("first name") ||
                    hint.includes("firstname")) {
                    return "Alex";
                }

                if (
                    hint.includes("last name") ||
                    hint.includes("lastname") ||
                    hint.includes("surname")) {
                    return "Tester";
                }

                if (
                    hint.includes("full name") ||
                    hint === "name") {
                    return "Alex Tester";
                }

                if (hint.includes("address")) {
                    return "200 N SPRING ST";
                }

                if (hint.includes("city")) {
                    return "Los Angeles";
                }

                if (
                    hint.includes("state") ||
                    hint.includes("province")) {
                    return "CA";
                }

                if (
                    hint.includes("zip") ||
                    hint.includes("postal")) {
                    return "90012";
                }

                return "Accessibility audit test";
            };

            const getFinancialTestValue = element => {
                const hint =
                    getFieldHint(element);

                if (
                    hint.includes("amount due") ||
                    hint.includes("refund amount") ||
                    hint.includes("amount claimed") ||
                    hint.includes("payment amount") ||
                    hint.includes("fee amount") ||
                    hint.includes("total amount")) {
                    return "100.00";
                }

                if (
                    hint.includes("ucs transaction") ||
                    hint.includes("transaction id") ||
                    hint.includes("transaction number")) {
                    return "TEST-TRANSACTION-001";
                }

                if (
                    hint.includes("cardholder") ||
                    hint.includes("name on card")) {
                    return "Alex Tester";
                }

                if (
                    element.getAttribute("autocomplete") ===
                        "cc-number" ||
                    hint.includes("card number") ||
                    hint.includes("credit card number") ||
                    hint.includes("debit card number")) {
                    return "4111111111111111";
                }

                if (
                    element.getAttribute("autocomplete") ===
                        "cc-csc" ||
                    hint.includes("cvv") ||
                    hint.includes("cvc") ||
                    hint.includes("security code")) {
                    return "123";
                }

                if (
                    element.getAttribute("autocomplete") ===
                        "cc-exp" ||
                    hint.includes("expiration") ||
                    hint.includes("expiry") ||
                    hint.includes("exp date")) {
                    return "12/30";
                }

                if (
                    hint.includes("check number") ||
                    hint.includes("check no") ||
                    hint.includes("check #") ||
                    hint.includes("cheque number")) {
                    return "100001";
                }

                if (
                    hint.includes("date fees paid") ||
                    hint.includes("payment date") ||
                    hint.includes("date paid")) {

                    const today =
                        new Date();

                    const month =
                        String(today.getMonth() + 1)
                            .padStart(2, "0");

                    const day =
                        String(today.getDate())
                            .padStart(2, "0");

                    const year =
                        today.getFullYear();

                    return element.type === "date"
                        ? `${year}-${month}-${day}`
                        : `${month}/${day}/${year}`;
                }

                return null;
            };

            for (const element of controls) {
                const description =
                    getDescription(element);

                if (!isVisibleAndEnabled(element)) {
                    skippedDescriptions.push(
                        `${description}: hidden or disabled`);

                    continue;
                }

                if (
                    element.hasAttribute("readonly") ||
                    element.getAttribute("aria-readonly") ===
                        "true") {
                    skippedDescriptions.push(
                        `${description}: read only`);

                    continue;
                }

                if (
                    element instanceof HTMLSelectElement) {
                    if (element.value) {
                        skippedDescriptions.push(
                            `${description}: already has a value`);

                        continue;
                    }

                    const option =
                        Array.from(element.options)
                            .find(candidate =>
                                !candidate.disabled &&
                                candidate.value !== "");

                    if (!option) {
                        skippedDescriptions.push(
                            `${description}: no selectable option`);

                        continue;
                    }

                    setNativeValue(
                        element,
                        option.value);

                    filledDescriptions.push(
                        `${description}: selected ${option.text}`);

                    continue;
                }

                if (
                    element instanceof
                        HTMLTextAreaElement) {
                    if (element.value.trim()) {
                        skippedDescriptions.push(
                            `${description}: already has a value`);

                        continue;
                    }

                    setNativeValue(
                        element,
                        "Accessibility audit test response.");

                    filledDescriptions.push(
                        description);

                    continue;
                }

                if (!(
                    element instanceof HTMLInputElement)) {
                    skippedDescriptions.push(
                        `${description}: unsupported control`);

                    continue;
                }

                const type =
                    (element.type || "text")
                        .toLowerCase();

                if ([
                    "hidden",
                    "button",
                    "submit",
                    "reset",
                    "image"
                ].includes(type)) {
                    continue;
                }

                if (type === "checkbox") {
                    if (element.checked) {
                        skippedDescriptions.push(
                            `${description}: already checked`);

                        continue;
                    }

                    setNativeChecked(
                        element,
                        true);

                    filledDescriptions.push(
                        description);

                    continue;
                }

                if (type === "radio") {
                        /*
                        * Radio groups are handled later by the C# Playwright code.
                        */
                    skippedDescriptions.push(
                        `${description}: handled later by Playwright`);

                    continue;
                }

                const currentValue =
                    element.value.trim();

                const fieldHint =
                    getFieldHint(element);

                const isDefaultMoneyValue =
                    (
                        fieldHint.includes("amount") ||
                        fieldHint.includes("fee") ||
                        fieldHint.includes("refund")
                    ) &&
                    /^[$,\s]*0(?:\.0+)?$/.test(
                        currentValue);

                if (
                    currentValue &&
                    !isDefaultMoneyValue) {
                    skippedDescriptions.push(
                        `${description}: already has a value`);

                    continue;
                }

                const financialTestValue =
                    getFinancialTestValue(element);

                if (financialTestValue !== null) {
                    setNativeValue(
                        element,
                        financialTestValue);

                    filledDescriptions.push(
                        `${description}: test value entered`);

                    continue;
                }

                let value;

                switch (type) {
                    case "email":
                        value =
                            "accessibility.audit@example.com";
                        break;

                    case "tel":
                        value =
                            "2135550100";
                        break;

                    case "url":
                        value =
                            "https://example.com";
                        break;

                    case "number":
                    case "range":
                    {
                        const minimum =
                            Number(element.min);

                        const maximum =
                            Number(element.max);

                        value =
                            Number.isFinite(minimum)
                                ? minimum
                                : 1;

                        if (
                            Number.isFinite(maximum) &&
                            value > maximum) {
                            value = maximum;
                        }

                        value =
                            String(value);

                        break;
                    }

                    case "date":
                        value =
                            element.min ||
                            new Date()
                                .toISOString()
                                .slice(0, 10);
                        break;

                    case "month":
                        value =
                            new Date()
                                .toISOString()
                                .slice(0, 7);
                        break;

                    case "time":
                        value = "09:00";
                        break;

                    case "datetime-local":
                        value =
                            new Date()
                                .toISOString()
                                .slice(0, 16);
                        break;

                    case "text":
                    case "search":
                        value =
                            getTextValue(element);
                        break;

                    default:
                        skippedDescriptions.push(
                            `${description}: unsupported input type ${type}`);

                        continue;
                }

                setNativeValue(
                    element,
                    value);

                filledDescriptions.push(
                    description);
            }
            return {
                FilledFieldCount:
                    filledDescriptions.length,

                SkippedFieldCount:
                    skippedDescriptions.length,

                RequiresManualInteraction: false,

                StopReason: null,

                FilledFieldDescriptions:
                    filledDescriptions,

                SkippedFieldDescriptions:
                    skippedDescriptions
            };
        })())
        """);

        AuthenticatedAuditFieldFillResult? result =
            System.Text.Json.JsonSerializer.Deserialize<
                AuthenticatedAuditFieldFillResult>(
                    resultJson);

        /*
        * Some address fields require selecting an autocomplete result after
        * typing. Wait for the suggestion list, then select the first visible
        * suggestion matching the entered address.
        */
        /*
        * Select an address from the site's autocomplete dropdown using
        * Playwright rather than a JavaScript element.click().
        */
        await Task.Delay(
            TimeSpan.FromMilliseconds(750));

        ILocator addressInputs =
            page.GetByLabel(
                "Address",
                new()
                {
                    Exact = false
                });

        ILocator? addressInput = null;

        for (
            int index = 0;
            index < await addressInputs.CountAsync();
            index++)
        {
            ILocator candidate =
                addressInputs.Nth(index);

            if (
                await candidate.IsVisibleAsync() &&
                await candidate.IsEditableAsync())
            {
                addressInput = candidate;
                break;
            }
        }

        /*
         * Fallback in case the Address label is not properly connected
         * to its input.
         */
        if (addressInput is null)
        {
            ILocator visibleInputs =
                page.Locator("input:visible");

            for (
                int index = 0;
                index < await visibleInputs.CountAsync();
                index++)
            {
                ILocator candidate =
                    visibleInputs.Nth(index);

                if (!await candidate.IsEditableAsync())
                {
                    continue;
                }

                string currentValue =
                    await candidate.InputValueAsync();

                if (string.Equals(
                    currentValue.Trim(),
                    "200 N SPRING ST",
                    StringComparison.OrdinalIgnoreCase))
                {
                    addressInput = candidate;
                    break;
                }
            }
        }

        if (addressInput is not null)
        {
            string addressValue =
                (await addressInput.InputValueAsync())
                    .Trim();

            if (!string.IsNullOrWhiteSpace(addressValue))
            {
                /*
                 * Wait for the site's address result to appear.
                 */
                await Task.Delay(
                    TimeSpan.FromMilliseconds(750));

                ILocator matchingResults =
                    page.GetByText(
                        addressValue,
                        new()
                        {
                            Exact = true
                        });

                bool resultSelected = false;

                /*
                 * Search backward because autocomplete results are commonly
                 * rendered after the input and hidden duplicate layouts.
                 */
                for (
                    int index =
                        await matchingResults.CountAsync() - 1;
                    index >= 0;
                    index--)
                {
                    ILocator matchingResult =
                        matchingResults.Nth(index);

                    if (await matchingResult.IsVisibleAsync())
                    {
                        await matchingResult.ClickAsync();

                        resultSelected = true;
                        break;
                    }
                }

                /*
                 * Keyboard fallback for autocomplete components that do not
                 * expose the suggestion as ordinary visible text.
                 */
                if (
                    !resultSelected &&
                    await addressInput.IsEditableAsync())
                {
                    await addressInput.ClickAsync();
                    await addressInput.PressAsync(
                        "ArrowDown");

                    await Task.Delay(
                        TimeSpan.FromMilliseconds(150));

                    await addressInput.PressAsync(
                        "Enter");
                }

                /*
                 * Give the page time to store the selected address and
                 * remove the validation error.
                 */
                await Task.Delay(
                    TimeSpan.FromMilliseconds(750));
            }
        }

        /*
        * Styled Yes/No controls may require a real browser click on their
        * visible labels. The earlier JavaScript may check the radio input,
        * but some applications do not process that as a user selection.
        */
        for (int radioGroupIndex = 0;
             radioGroupIndex < 25;
             radioGroupIndex++)
        {
            string radioChoiceSelector =
                await page.EvaluateAsync<string>(
                    """
            () => {
                const processedAttribute =
                    "data-city-audit-radio-processed";

                const clickAttribute =
                    "data-city-audit-radio-click";

                const isVisible = element => {
                    if (!(element instanceof HTMLElement)) {
                        return false;
                    }

                    const style =
                        window.getComputedStyle(element);

                    const rectangle =
                        element.getBoundingClientRect();

                    return (
                        style.display !== "none" &&
                        style.visibility !== "hidden" &&
                        rectangle.width > 0 &&
                        rectangle.height > 0
                    );
                };

                /*
                 * Remove the temporary click marker from the
                 * previously processed option.
                 */
                document
                    .querySelectorAll(
                        `[${clickAttribute}]`)
                    .forEach(element =>
                        element.removeAttribute(
                            clickAttribute));

                const radios =
                    Array.from(
                        document.querySelectorAll(
                            'input[type="radio"]'));

                const visitedGroups =
                    new Set();

                for (const radio of radios) {
                    const groupName =
                        radio.name ||
                        radio.id;

                    if (!groupName) {
                        continue;
                    }

                    const formIdentifier =
                        radio.form?.id ||
                        radio.form?.name ||
                        "";

                    const groupKey =
                        `${formIdentifier}|${groupName}`;

                    if (visitedGroups.has(groupKey)) {
                        continue;
                    }

                    visitedGroups.add(groupKey);

                    const group =
                        radios.filter(candidate => {
                            const candidateFormIdentifier =
                                candidate.form?.id ||
                                candidate.form?.name ||
                                "";

                            return (
                                candidateFormIdentifier ===
                                    formIdentifier &&
                                (
                                    candidate.name ||
                                    candidate.id
                                ) === groupName
                            );
                        });

                    /*
                     * Each group is processed only once during
                     * this FillSafeFieldsAsync call.
                     */
                    if (
                        group.some(candidate =>
                            candidate.hasAttribute(
                                processedAttribute))
                    ) {
                        continue;
                    }

                    group.forEach(candidate =>
                        candidate.setAttribute(
                            processedAttribute,
                            "true"));

                    /*
                     * Preserve the choice already made by the
                     * filler. If none was made, use the first
                     * enabled option, matching existing behavior.
                     */
                    const getRadioOptionText = candidate => {
                        let labelText = "";

                        if (candidate.id) {
                            labelText =
                                document.querySelector(
                                    `label[for="${
                                        CSS.escape(candidate.id)
                                    }"]`)
                                ?.textContent || "";
                        }

                        if (!labelText) {
                            labelText =
                                candidate.closest("label")
                                    ?.textContent || "";
                        }

                        return [
                            labelText,
                            candidate.getAttribute("aria-label"),
                            candidate.value
                        ]
                            .filter(Boolean)
                            .join(" ")
                            .replace(/\s+/g, " ")
                            .trim()
                            .toLowerCase();
                        };

                        /*
                        * Prefer No for ordinary Yes/No questions. This avoids opening
                        * additional conditional fields during a demonstration audit.
                        */
                        const preferredNoRadio =
                            group.find(candidate => {
                                if (candidate.disabled) {
                                    return false;
                                }

                                const optionText =
                                    getRadioOptionText(candidate);

                                return (
                                    optionText === "no" ||
                                    optionText.startsWith("no ") ||
                                    candidate.value
                                        ?.trim()
                                        .toLowerCase() === "false" ||
                                    candidate.value === "0"
                                );
                            });

                        const selectedRadio =
                            preferredNoRadio ||
                            group.find(candidate =>
                                candidate.checked &&
                                !candidate.disabled) ||
                            group.find(candidate =>
                                !candidate.disabled);

                    if (!selectedRadio) {
                        continue;
                    }

                    let clickableElement = null;

                    if (selectedRadio.id) {
                        clickableElement =
                            document.querySelector(
                                `label[for="${
                                    CSS.escape(
                                        selectedRadio.id)
                                }"]`);
                    }

                    if (!clickableElement) {
                        clickableElement =
                            selectedRadio.closest(
                                "label");
                    }

                    /*
                     * Styled radio buttons usually display the
                     * label while hiding the input.
                     */
                    if (
                        clickableElement &&
                        isVisible(clickableElement)
                    ) {
                        clickableElement.setAttribute(
                            clickAttribute,
                            "true");

                        return `[${clickAttribute}="true"]`;
                    }

                    if (isVisible(selectedRadio)) {
                        selectedRadio.setAttribute(
                            clickAttribute,
                            "true");

                        return `[${clickAttribute}="true"]`;
                    }
                }

                return "";
            }
            """);

            if (string.IsNullOrWhiteSpace(
                radioChoiceSelector))
            {
                break;
            }

            ILocator radioChoiceLocator =
                page.Locator(radioChoiceSelector);

            try
            {
                await radioChoiceLocator.ClickAsync(
                    new()
                    {
                        Timeout = 3000
                    });

                await Task.Delay(
                    TimeSpan.FromMilliseconds(200));
            }
            catch (PlaywrightException)
            {
                /*
                 * Do not fail the entire audit because one optional
                 * radio group could not be clicked.
                 */
            }
        }

        /*
         * Remove temporary attributes after all groups are processed.
         */
        await page.EvaluateAsync(
            """
            () => {
            document
                .querySelectorAll(
                    '[data-city-audit-radio-processed], ' +
                    '[data-city-audit-radio-click]')
                .forEach(element => {
                    element.removeAttribute(
                        "data-city-audit-radio-processed");

                    element.removeAttribute(
                        "data-city-audit-radio-click");
                });
            }
            """);

        return result ??
            throw new InvalidOperationException(
                "The field-filling result could not be read.");
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
                            LimitLength(nodeResult.FailureSummary, 4000),

                        ElementFixGuidance =
                            LimitLength(
                                    BuildElementFixGuidance(
                                    findingResult.RuleId),
                                4000)
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
