using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using CityWebsiteAuditDashboard.Services.Remediation;
using CityWebsiteAuditDashboard.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

namespace CityWebsiteAuditDashboard.Controllers;

public sealed class AccessibilityRemediationsController : Controller
{
    private readonly AccessibilityRemediationService _remediationService;
    private readonly ApplicationDbContext _dbContext;
    private readonly AccessibilityRemediationRetestService _remediationRetestService;
    private readonly IAuthenticatedAuditService _authenticatedAuditService;

    public AccessibilityRemediationsController(
        AccessibilityRemediationService remediationService,
        AccessibilityRemediationRetestService remediationRetestService,
        IAuthenticatedAuditService authenticatedAuditService,
        ApplicationDbContext dbContext)
    {
        _remediationService = remediationService;
        _remediationRetestService = remediationRetestService;
        _authenticatedAuditService = authenticatedAuditService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
    string? statusFilter,
    string? applicationFilter,
    string? severityFilter,
    string? assigneeFilter)
    {
        var remediationItems =
            await _dbContext.AccessibilityRemediationItems
                .AsNoTracking()
                .Include(item => item.FindingOccurrences)
                    .ThenInclude(occurrence =>
                        occurrence.AuthenticatedAuditFinding)
                        .ThenInclude(finding =>
                            finding.AuthenticatedAuditStep)
                            .ThenInclude(step =>
                                step.AuthenticatedAuditRun)
                .OrderByDescending(item => item.UpdatedAt)
                .ToListAsync();

        AccessibilityRemediationIndexViewModel viewModel = new()
        {
            StatusFilter = statusFilter,
            ApplicationFilter = applicationFilter,
            SeverityFilter = severityFilter,
            AssigneeFilter = assigneeFilter,

            TotalCount = remediationItems.Count,

            OpenCount = remediationItems.Count(item =>
                item.Status == AccessibilityRemediationStatus.Open),

            InProgressCount = remediationItems.Count(item =>
                item.Status == AccessibilityRemediationStatus.InProgress),

            FixedCount = remediationItems.Count(item =>
                item.Status == AccessibilityRemediationStatus.Fixed),

            VerifiedCount = remediationItems.Count(item =>
                item.Status == AccessibilityRemediationStatus.Verified),

            WontFixCount = remediationItems.Count(item =>
                item.Status == AccessibilityRemediationStatus.WontFix)
        };

        foreach (var item in remediationItems)
        {
            var occurrence = item.FindingOccurrences
                .OrderBy(occurrence => occurrence.LinkedAt)
                .FirstOrDefault();

            if (occurrence is null)
            {
                continue;
            }

            var finding = occurrence.AuthenticatedAuditFinding;
            var step = finding.AuthenticatedAuditStep;
            var run = step.AuthenticatedAuditRun;

            viewModel.Items.Add(
                new AccessibilityRemediationListItemViewModel
                {
                    Id = item.Id,
                    Status = item.Status.ToString(),
                    AssignedTo = item.AssignedTo,
                    CreatedAt = item.CreatedAt,
                    UpdatedAt = item.UpdatedAt,

                    ApplicationName = run.ApplicationName,
                    StepName = step.StepName,
                    Url = step.Url,

                    FindingType = finding.FindingType,
                    RuleId = finding.RuleId,
                    Impact = finding.Impact,
                    WcagLevel = finding.WcagLevel,
                    AffectedElementCount =
                        finding.AffectedElementCount,

                    DetectedAt = step.ScannedAt
                });
        }

        viewModel.ApplicationOptions =
            viewModel.Items
                .Select(item => item.ApplicationName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();

        viewModel.AssigneeOptions =
            viewModel.Items
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.AssignedTo))
                .Select(item => item.AssignedTo!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            viewModel.Items = viewModel.Items
                .Where(item =>
                    string.Equals(
                        item.Status,
                        statusFilter,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(applicationFilter))
        {
            viewModel.Items = viewModel.Items
                .Where(item =>
                    string.Equals(
                        item.ApplicationName,
                        applicationFilter,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(severityFilter))
        {
            viewModel.Items = viewModel.Items
                .Where(item =>
                    string.Equals(
                        item.Impact,
                        severityFilter,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(assigneeFilter))
        {
            if (assigneeFilter == "Unassigned")
            {
                viewModel.Items = viewModel.Items
                    .Where(item =>
                        string.IsNullOrWhiteSpace(item.AssignedTo))
                    .ToList();
            }
            else
            {
                viewModel.Items = viewModel.Items
                    .Where(item =>
                        string.Equals(
                            item.AssignedTo,
                            assigneeFilter,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var remediationItem =
            await _dbContext.AccessibilityRemediationItems
                .AsNoTracking()
                .Include(item => item.History)
                .Include(item => item.Retests)
                .Include(item => item.FindingOccurrences)
                    .ThenInclude(occurrence =>
                        occurrence.AuthenticatedAuditFinding)
                        .ThenInclude(finding => finding.Nodes)
                .Include(item => item.FindingOccurrences)
                    .ThenInclude(occurrence =>
                        occurrence.AuthenticatedAuditFinding)
                        .ThenInclude(finding =>
                            finding.AuthenticatedAuditStep)
                            .ThenInclude(step =>
                                step.AuthenticatedAuditRun)
                .FirstOrDefaultAsync(item => item.Id == id);

        if (remediationItem is null)
        {
            return NotFound();
        }

        var occurrence = remediationItem.FindingOccurrences
            .OrderBy(occurrence => occurrence.LinkedAt)
            .FirstOrDefault();

        if (occurrence is null)
        {
            return NotFound();
        }

        var finding = occurrence.AuthenticatedAuditFinding;
        var step = finding.AuthenticatedAuditStep;
        var run = step.AuthenticatedAuditRun;

        AccessibilityRemediationDetailsViewModel viewModel = new()
        {
            Id = remediationItem.Id,
            Status = remediationItem.Status.ToString(),
            AssignedTo = remediationItem.AssignedTo,
            CreatedAt = remediationItem.CreatedAt,
            UpdatedAt = remediationItem.UpdatedAt,

            ApplicationName = run.ApplicationName,
            AuditRunId = run.Id,

            StepNumber = step.StepNumber,
            StepName = step.StepName,
            Url = step.Url,
            PageTitle = step.PageTitle,
            Heading = step.Heading,
            DetectedAt = step.ScannedAt,

            FindingType = finding.FindingType,
            RuleId = finding.RuleId,
            Impact = finding.Impact,
            WcagLevel = finding.WcagLevel,
            WcagTags = finding.WcagTags,
            Help = finding.Help,
            Description = finding.Description,
            HelpUrl = finding.HelpUrl,
            AffectedElementCount = finding.AffectedElementCount,

            Nodes = finding.Nodes
                .Select(node =>
                    new AccessibilityRemediationNodeViewModel
                    {
                        Target = node.Target,
                        Html = node.Html,
                        FailureSummary = node.FailureSummary,
                        ElementFixGuidance =
                            node.ElementFixGuidance
                    })
                .ToList(),

            Retests = remediationItem.Retests
                .OrderByDescending(retest => retest.RetestedAt)
                .Select(retest =>
                    new AccessibilityRemediationRetestViewModel
                    {
                        Id = retest.Id,

                        Result = retest.Result.ToString(),

                        RetestType =
                            retest.RetestType,

                        AuthenticatedAuditRunId =
                            retest.AuthenticatedAuditRunId,

                        MatchMethod = retest.MatchMethod,

                        MatchConfidence = retest.MatchConfidence,

                        RetestedAt = retest.RetestedAt,

                        Notes = retest.Notes,

                        RetestedBy = retest.RetestedBy,

                        AuthenticatedAuditStepId =
                            retest.AuthenticatedAuditStepId,

                        MatchedAuthenticatedAuditFindingId =
                            retest.MatchedAuthenticatedAuditFindingId
                    })
                .ToList(),

            History = remediationItem.History
                .OrderByDescending(history => history.ChangedAt)
                .Select(history =>
                    new AccessibilityRemediationHistoryViewModel
                    {
                        EventType = history.EventType,

                        PreviousStatus =
                            history.PreviousStatus?.ToString(),

                        NewStatus =
                            history.NewStatus?.ToString(),

                        PreviousAssignee =
                            history.PreviousAssignee,

                        NewAssignee =
                            history.NewAssignee,

                        Notes = history.Notes,

                        ChangedAt = history.ChangedAt,

                        ChangedBy = history.ChangedBy
                    })
                .ToList()
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
    int id,
    AccessibilityRemediationStatus status,
    string? assignedTo,
    string? notes)
    {
        try
        {
            await _remediationService.UpdateAsync(
                id,
                status,
                assignedTo,
                notes);

            TempData["SuccessMessage"] =
                "Remediation item updated successfully.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] =
                exception.Message;
        }

        return RedirectToAction(
            nameof(Details),
            new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(
    int findingId,
    int auditRunId)
    {
        AccessibilityRemediationItem item =
            await _remediationService.CreateForFindingAsync(
                findingId);

        return RedirectToAction(
            nameof(Details),
            new { id = item.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetestCurrentState(
    int id,
    string? notes,
    CancellationToken cancellationToken)
    {
        try
        {
            AuthenticatedAuditSessionResult? activeSession =
                _authenticatedAuditService.GetActiveSession();

            if (activeSession is null)
            {
                TempData["ErrorMessage"] =
                    "No authenticated audit browser session is currently active. " +
                    "Start an authenticated audit, log in, and navigate to the " +
                    "page or workflow state that needs to be retested.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }

            /*
             * Reuse the normal authenticated scanner.
             * This saves a new AuthenticatedAuditStep using the current
             * logged-in Playwright browser state.
             */
            AuthenticatedAuditStepResult scanResult =
                await _authenticatedAuditService.ScanCurrentStepAsync(
                    activeSession.SessionId,
                    cancellationToken);

            /*
             * ScanCurrentStepAsync returns the step number, while the
             * remediation matcher needs the database ID of the newly
             * saved AuthenticatedAuditStep.
             */
            int? savedStepId =
                await _dbContext.AuthenticatedAuditSteps
                    .AsNoTracking()
                    .Where(step =>
                        step.AuthenticatedAuditRunId ==
                            activeSession.AuditRunId &&
                        step.StepNumber ==
                            scanResult.StepNumber)
                    .Select(step => (int?)step.Id)
                    .SingleOrDefaultAsync(
                        cancellationToken);

            if (!savedStepId.HasValue)
            {
                throw new InvalidOperationException(
                    "The newly scanned authenticated audit step could not be found.");
            }

            AccessibilityRemediationRetest retest =
                await _remediationRetestService.RecordRetestAsync(
                    id,
                    savedStepId.Value,
                    notes,
                    cancellationToken: cancellationToken);

            TempData["SuccessMessage"] =
                retest.Result switch
                {
                    AccessibilityRemediationRetestResult.Detected =>
                        "Retest completed. The tracked accessibility issue " +
                        "is still detected.",

                    AccessibilityRemediationRetestResult.NotDetected =>
                        "Retest completed. The tracked issue was not detected. " +
                        "It is still awaiting verification.",

                    AccessibilityRemediationRetestResult.Inconclusive =>
                        "Retest completed, but the result was inconclusive. " +
                        "Confirm that the authenticated browser is on the " +
                        "same page or workflow state.",

                    AccessibilityRemediationRetestResult.Failed =>
                        "The retest scan did not complete successfully.",

                    _ =>
                        "Retest completed."
                };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TempData["ErrorMessage"] =
                "The remediation retest could not be completed. " +
                exception.Message;
        }

        return RedirectToAction(
            nameof(Details),
            new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(
    int id,
    string? notes,
    CancellationToken cancellationToken)
    {
        try
        {
            await _remediationRetestService.VerifyAsync(
                id,
                notes,
                cancellationToken: cancellationToken);

            TempData["SuccessMessage"] =
                "The remediation has been verified successfully.";
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] =
                exception.Message;
        }

        return RedirectToAction(
            nameof(Details),
            new { id });
    }
}
