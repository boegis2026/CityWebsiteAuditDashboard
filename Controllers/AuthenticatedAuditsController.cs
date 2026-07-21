using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using CityWebsiteAuditDashboard.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CityWebsiteAuditDashboard.Controllers;

/// <summary>
/// Provides the dashboard actions used to start, scan, and stop a manually
/// authenticated Playwright audit.
///
/// The controller never handles Playwright objects directly. All live browser
/// state remains inside IAuthenticatedAuditService.
/// </summary>
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class AuthenticatedAuditsController : Controller
{
    private readonly IAuthenticatedAuditService _authenticatedAuditService;
    private readonly ILogger<AuthenticatedAuditsController> _logger;
    private readonly ApplicationDbContext _dbContext;

    public AuthenticatedAuditsController(
    IAuthenticatedAuditService authenticatedAuditService,
    ApplicationDbContext dbContext,
    ILogger<AuthenticatedAuditsController> logger)
    {
        _authenticatedAuditService = authenticatedAuditService;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
    CancellationToken cancellationToken)
    {
        string? statusMessage =
            TempData["AuthenticatedAuditStatus"] as string;

        AuthenticatedAuditBatchResult? lastBatchResult = null;

        string? batchResultJson =
            TempData["AuthenticatedAuditBatchResult"] as string;

        if (!string.IsNullOrWhiteSpace(batchResultJson))
        {
            try
            {
                lastBatchResult =
                    JsonSerializer.Deserialize<AuthenticatedAuditBatchResult>(
                        batchResultJson);
            }
            catch (JsonException exception)
            {
                /*
                 * A display-only TempData problem should not prevent the main
                 * authenticated session from being restored.
                 */
                _logger.LogWarning(
                    exception,
                    "The latest authenticated batch summary could not be restored.");
            }
        }

        AuthenticatedAuditSessionResult? activeSession =
            _authenticatedAuditService.GetActiveSession();

        if (activeSession is null)
        {
            return View(
                new AuthenticatedAuditDashboardViewModel
                {
                    StatusMessage = statusMessage
                });
        }

        /*
         * POST actions redirect back to this GET action. Reload the most recently
         * saved step from SQL Server so refreshing the browser never repeats a
         * Start or Scan form submission.
         */
        AuthenticatedAuditStepResult? latestStep =
            await _dbContext.AuthenticatedAuditSteps
                .AsNoTracking()
                .Where(step =>
                    step.AuthenticatedAuditRunId ==
                    activeSession.AuditRunId)
                .OrderByDescending(step => step.StepNumber)
                .Select(step => new AuthenticatedAuditStepResult
                {
                    StepNumber = step.StepNumber,
                    StepName = step.StepName,
                    Url = step.Url,
                    PageTitle = step.PageTitle,
                    Heading = step.Heading,
                    DomFingerprint = step.DomFingerprint,
                    ScannedAt = step.ScannedAt,
                    VisibleFormCount = step.VisibleFormCount,
                    VisibleFieldCount = step.VisibleFieldCount,
                    VisibleButtonCount = step.VisibleButtonCount,
                    ViolationRuleCount = step.ViolationRuleCount,
                    AffectedElementCount = step.AffectedElementCount,
                    NeedsReviewRuleCount = step.NeedsReviewRuleCount,
                    PassedRuleCount = step.PassedRuleCount,
                    ScanSucceeded = step.ScanSucceeded,
                    ErrorMessage = step.ErrorMessage
                })
                .FirstOrDefaultAsync(cancellationToken);

        AuthenticatedAuditDashboardViewModel model =
            CreateActiveSessionViewModel(
                activeSession,
                statusMessage ??
                "The existing authenticated browser session is still active.");

        model.LastBatchResult = lastBatchResult;

        model.LastStepResult = latestStep;

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> History(
    CancellationToken cancellationToken)
    {
        /*
         * This is a read only dashboard query, so change tracking is disabled.
         * The summary is projected directly in SQL rather than loading every
         * AuthenticatedAuditStep entity into memory.
         */
        List<AuthenticatedAuditRunSummaryViewModel> runs =
            await _dbContext.AuthenticatedAuditRuns
                .AsNoTracking()
                .OrderByDescending(run => run.StartedAt)
                .Select(run => new AuthenticatedAuditRunSummaryViewModel
                {
                    Id = run.Id,
                    ApplicationName = run.ApplicationName,
                    StartingUrl = run.StartingUrl,
                    AccessibilityEngine = run.AccessibilityEngine,
                    StartedAt = run.StartedAt,
                    CompletedAt = run.CompletedAt,
                    Status = run.Status,

                    StepCount = run.Steps.Count,

                    SuccessfulStepCount =
                        run.Steps.Count(step => step.ScanSucceeded),

                    FailedStepCount =
                        run.Steps.Count(step => !step.ScanSucceeded),

                    /*
                     * A run may have no final marker when the browser crashed,
                     * the application stopped unexpectedly, or no step was
                     * scanned before the session ended.
                     */
                    FinalStepNumber = run.Steps
                        .Where(step => step.WasFinalStep)
                        .Select(step => (int?)step.StepNumber)
                        .FirstOrDefault(),

                    ErrorMessage = run.ErrorMessage
                })
                .ToListAsync(cancellationToken);

        return View(
            new AuthenticatedAuditHistoryViewModel
            {
                Runs = runs
            });
    }

    [HttpGet]
    public async Task<IActionResult> Details(
    int id,
    CancellationToken cancellationToken)
    {
        /*
         * Load the selected run with its scanned steps and rule-level findings.
         * AsNoTracking is appropriate because this page only displays saved data.
         */
        AuthenticatedAuditRun? auditRun =
            await _dbContext.AuthenticatedAuditRuns
                .AsNoTracking()
                .Include(run => run.Steps)
                    .ThenInclude(step => step.Findings)
                .SingleOrDefaultAsync(
                    run => run.Id == id,
                    cancellationToken);

        if (auditRun is null)
        {
            return NotFound();
        }

        /*
         * Map database entities into dashboard-specific view models.
         *
         * This mapping occurs after the SQL query completes, which avoids making
         * EF Core translate a large nested projection into SQL.
         */
        var model = new AuthenticatedAuditDetailsViewModel
        {
            Id = auditRun.Id,
            ApplicationName = auditRun.ApplicationName,
            StartingUrl = auditRun.StartingUrl,
            AccessibilityEngine = auditRun.AccessibilityEngine,
            StartedAt = auditRun.StartedAt,
            CompletedAt = auditRun.CompletedAt,
            Status = auditRun.Status,
            ErrorMessage = auditRun.ErrorMessage,

            Steps = auditRun.Steps
                .OrderBy(step => step.StepNumber)
                .Select(step =>
                    new AuthenticatedAuditStepDetailsViewModel
                    {
                        Id = step.Id,
                        StepNumber = step.StepNumber,
                        StepName = step.StepName,
                        Url = step.Url,
                        PageTitle = step.PageTitle,
                        Heading = step.Heading,
                        DomFingerprint = step.DomFingerprint,
                        ScannedAt = step.ScannedAt,
                        VisibleFormCount = step.VisibleFormCount,
                        VisibleFieldCount = step.VisibleFieldCount,
                        VisibleButtonCount = step.VisibleButtonCount,
                        ViolationRuleCount = step.ViolationRuleCount,
                        AffectedElementCount = step.AffectedElementCount,
                        NeedsReviewRuleCount = step.NeedsReviewRuleCount,
                        PassedRuleCount = step.PassedRuleCount,
                        ScanSucceeded = step.ScanSucceeded,
                        WasFinalStep = step.WasFinalStep,
                        ErrorMessage = step.ErrorMessage,

                        /*
                         * Only safe rule-level information is displayed.
                         * Element HTML and entered form values were never stored.
                         */
                        Findings = step.Findings
                            .OrderBy(finding => finding.FindingType)
                            .ThenBy(finding => finding.RuleId)
                            .Select(finding =>
                                new AuthenticatedAuditFindingDetailsViewModel
                                {
                                    Id = finding.Id,
                                    FindingType = finding.FindingType,
                                    RuleId = finding.RuleId,
                                    Impact = finding.Impact,
                                    Help = finding.Help,
                                    Description = finding.Description,
                                    HelpUrl = finding.HelpUrl,
                                    AffectedElementCount =
                                        finding.AffectedElementCount
                                })
                            .ToList()
                    })
                .ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(
    [Bind(Prefix = "StartInput")]
    AuthenticatedAuditStartInputModel input,
    CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(
                "Index",
                new AuthenticatedAuditDashboardViewModel
                {
                    StartInput = input
                });
        }

        try
        {
            AuthenticatedAuditSessionResult result =
                await _authenticatedAuditService.StartSessionAsync(
                    new AuthenticatedAuditStartRequest
                    {
                        ApplicationName = input.ApplicationName,
                        StartingUrl = input.StartingUrl
                    },
                    cancellationToken);

            /*
            * Redirect after a successful POST so refreshing the dashboard does not
            * submit Start again and launch another Edge window.
            */
            TempData["AuthenticatedAuditStatus"] =
                "The browser is open. Log in manually, navigate to the first " +
                "protected state, and then select Scan Current Step.";

            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Allow ASP.NET Core to handle an abandoned or cancelled request.
            throw;
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(
                string.Empty,
                exception.Message);

            return View(
                "Index",
                new AuthenticatedAuditDashboardViewModel
                {
                    StartInput = input
                });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(
                string.Empty,
                exception.Message);

            AuthenticatedAuditSessionResult? activeSession =
                _authenticatedAuditService.GetActiveSession();

            if (activeSession is not null)
            {
                return View(
                    "Index",
                    CreateActiveSessionViewModel(activeSession));
            }

            return View(
                "Index",
                new AuthenticatedAuditDashboardViewModel
                {
                    StartInput = input
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The authenticated audit browser could not be started.");

            ModelState.AddModelError(
                string.Empty,
                "The authenticated browser could not be started. " +
                exception.Message);

            return View(
                "Index",
                new AuthenticatedAuditDashboardViewModel
                {
                    StartInput = input
                });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Scan(
        AuthenticatedAuditSessionInputModel input,
        CancellationToken cancellationToken)
    {
        AuthenticatedAuditDashboardViewModel model =
            CreateActiveSessionViewModel(input);

        if (input.SessionId == Guid.Empty)
        {
            ModelState.AddModelError(
                string.Empty,
                "The authenticated audit session ID is missing.");

            return View("Index", model);
        }

        try
        {
            AuthenticatedAuditStepResult stepResult =
                await _authenticatedAuditService.ScanCurrentStepAsync(
                    input.SessionId,
                    cancellationToken);

            /*
            * The Index GET action reloads the latest saved step from SQL Server.
            * Redirecting prevents a browser refresh from scanning the same state twice.
            */
            TempData["AuthenticatedAuditStatus"] =
                stepResult.ScanSucceeded
                    ? $"Step {stepResult.StepNumber} was scanned and saved."
                    : $"Step {stepResult.StepNumber} was saved, but its " +
                      "accessibility scan did not complete.";

            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KeyNotFoundException exception)
        {
            /*
             * The browser may have been manually closed, the application may
             * have restarted, or the in memory session may already be stopped.
             */
            ModelState.AddModelError(
                string.Empty,
                exception.Message);

            model.SessionId = null;

            return View("Index", model);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Authenticated audit session {SessionId} could not scan " +
                "the current rendered state.",
                input.SessionId);

            ModelState.AddModelError(
                string.Empty,
                "The current rendered state could not be scanned. " +
                exception.Message);

            return View("Index", model);
        }
    }

    private static AuthenticatedAuditDashboardViewModel
    CreateActiveSessionViewModel(
        AuthenticatedAuditSessionResult session,
        string? statusMessage = null)
    {
        return new AuthenticatedAuditDashboardViewModel
        {
            SessionId = session.SessionId,
            ApplicationName = session.ApplicationName,
            StartingUrl = session.StartingUrl,
            AccessibilityEngine = session.AccessibilityEngine,
            StartedAt = session.StartedAt,
            StatusMessage = statusMessage
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScanBatch(
    [Bind(Prefix = "BatchInput")]
    AuthenticatedAuditBatchInputModel input,
    CancellationToken cancellationToken)
    {
        /*
         * Ignore blank lines and remove spaces around each pasted URL.
         */
        string[] urls =
            (input.Urls ?? string.Empty)
                .Split(
                    new[] { "\r\n", "\n", "\r" },
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

        if (input.NumberOfUrls.HasValue &&
            input.NumberOfUrls.Value != urls.Length)
        {
            ModelState.AddModelError(
                "BatchInput.NumberOfUrls",
                $"You entered {input.NumberOfUrls.Value}, but pasted " +
                $"{urls.Length} non-empty URL(s).");
        }

        for (int index = 0; index < urls.Length; index++)
        {
            string url = urls[index];

            bool validUrl =
                Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out Uri? parsedUri) &&
                (parsedUri.Scheme == Uri.UriSchemeHttp ||
                 parsedUri.Scheme == Uri.UriSchemeHttps);

            if (!validUrl)
            {
                ModelState.AddModelError(
                    "BatchInput.Urls",
                    $"Line {index + 1} is not a valid HTTP or HTTPS URL: {url}");
            }
        }

        AuthenticatedAuditSessionResult? activeSession =
            _authenticatedAuditService.GetActiveSession();

        if (activeSession is null ||
            activeSession.SessionId != input.SessionId)
        {
            TempData["AuthenticatedAuditStatus"] =
                "The authenticated browser session is no longer available. " +
                "Start a new session and sign in again.";

            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            /*
             * Validation failures return the form directly because no browser
             * navigation or accessibility scan has occurred yet.
             */
            AuthenticatedAuditDashboardViewModel invalidModel =
                CreateActiveSessionViewModel(activeSession);

            invalidModel.BatchInput = input;

            return View("Index", invalidModel);
        }

        try
        {
            AuthenticatedAuditBatchResult result =
                await _authenticatedAuditService.ScanBatchAsync(
                    input.SessionId,
                    urls,
                    cancellationToken);

            /*
             * Redirect after the successful POST so refreshing the dashboard does
             * not run the same authenticated batch a second time.
             */
            TempData["AuthenticatedAuditStatus"] =
                $"Authenticated batch completed: " +
                $"{result.SucceededCount} succeeded and " +
                $"{result.FailedCount} failed out of " +
                $"{result.RequestedCount} URL(s).";

            /*
             * Store the small result summary for one redirect so the dashboard can show
             * what happened for each URL without rerunning the batch after a refresh.
             *
             * The full audit steps and accessibility findings are already permanently
             * stored in SQL Server.
             */
            TempData["AuthenticatedAuditBatchResult"] =
                JsonSerializer.Serialize(result);

            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            TempData["AuthenticatedAuditStatus"] =
                "The authenticated batch was cancelled before it completed.";

            return RedirectToAction(nameof(Index));
        }
        catch (KeyNotFoundException exception)
        {
            TempData["AuthenticatedAuditStatus"] =
                exception.Message;

            return RedirectToAction(nameof(Index));
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(
                string.Empty,
                exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Authenticated batch scanning failed for session {SessionId}.",
                input.SessionId);

            ModelState.AddModelError(
                string.Empty,
                "The authenticated batch could not be completed. " +
                "Review the application logs for more information.");
        }

        AuthenticatedAuditSessionResult? remainingSession =
            _authenticatedAuditService.GetActiveSession();

        if (remainingSession is null)
        {
            return View(
                "Index",
                new AuthenticatedAuditDashboardViewModel());
        }

        AuthenticatedAuditDashboardViewModel errorModel =
            CreateActiveSessionViewModel(remainingSession);

        errorModel.BatchInput = input;

        return View("Index", errorModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stop(
        AuthenticatedAuditSessionInputModel input,
        CancellationToken cancellationToken)
    {
        AuthenticatedAuditDashboardViewModel model =
            CreateActiveSessionViewModel(input);

        if (input.SessionId == Guid.Empty)
        {
            ModelState.AddModelError(
                string.Empty,
                "The authenticated audit session ID is missing.");

            return View("Index", model);
        }

        try
        {
            await _authenticatedAuditService.StopSessionAsync(
                input.SessionId,
                input.MarkLastStepAsFinal,
                cancellationToken);

            /*
            * Redirecting prevents refresh from attempting to stop the same session again.
            */
            TempData["AuthenticatedAuditStatus"] =
                "The authenticated audit session was completed and " +
                "the Playwright browser was closed.";

            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KeyNotFoundException exception)
        {
            ModelState.AddModelError(
                string.Empty,
                exception.Message);

            model.SessionId = null;

            return View("Index", model);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Authenticated audit session {SessionId} could not be stopped.",
                input.SessionId);

            ModelState.AddModelError(
                string.Empty,
                "The authenticated audit session could not be stopped. " +
                exception.Message);

            return View("Index", model);
        }
    }

    private static AuthenticatedAuditDashboardViewModel
    CreateActiveSessionViewModel(
        AuthenticatedAuditSessionInputModel input)
    {
        return new AuthenticatedAuditDashboardViewModel
        {
            SessionId = input.SessionId,
            ApplicationName = input.ApplicationName,
            StartingUrl = input.StartingUrl,
            AccessibilityEngine = string.IsNullOrWhiteSpace(
                input.AccessibilityEngine)
                    ? "axe-core"
                    : input.AccessibilityEngine,
            StartedAt = input.StartedAt,

            /*
             * Keep the authenticated batch connected to the same live
             * Playwright browser session.
             */
            BatchInput = new AuthenticatedAuditBatchInputModel
            {
                SessionId = input.SessionId
            }
        };
    }
}
