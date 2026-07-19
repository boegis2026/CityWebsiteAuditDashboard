using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using CityWebsiteAuditDashboard.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public IActionResult Index()
    {
        AuthenticatedAuditSessionResult? activeSession =
            _authenticatedAuditService.GetActiveSession();

        if (activeSession is null)
        {
            return View(new AuthenticatedAuditDashboardViewModel());
        }

        /*
         * Restore the visible session controls after a refresh or after the user
         * visits another dashboard page. The live browser remained in the
         * singleton service throughout that navigation.
         */
        return View(
            CreateActiveSessionViewModel(
                activeSession,
                "The existing authenticated browser session is still active."));
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

            return View(
                "Index",
                new AuthenticatedAuditDashboardViewModel
                {
                    SessionId = result.SessionId,
                    ApplicationName = result.ApplicationName,
                    StartingUrl = result.StartingUrl,
                    AccessibilityEngine = result.AccessibilityEngine,
                    StartedAt = result.StartedAt,
                    StatusMessage =
                        "The browser is open. Log in manually, navigate to " +
                        "the first protected state, and then select Scan Current Step."
                });
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

            model.LastStepResult = stepResult;

            model.StatusMessage = stepResult.ScanSucceeded
                ? $"Step {stepResult.StepNumber} was scanned and saved."
                : $"Step {stepResult.StepNumber} was saved, but its " +
                  "accessibility scan did not complete.";

            return View("Index", model);
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
             * Clearing SessionId causes the page to return to its inactive
             * state. The completed run and its steps remain in SQL Server.
             */
            model.SessionId = null;
            model.LastStepResult = null;
            model.StatusMessage =
                "The authenticated audit session was completed and " +
                "the Playwright browser was closed.";

            return View("Index", model);
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
            StartedAt = input.StartedAt
        };
    }
}
