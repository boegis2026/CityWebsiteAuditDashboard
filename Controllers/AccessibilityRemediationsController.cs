using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using CityWebsiteAuditDashboard.Services.Remediation;
using CityWebsiteAuditDashboard.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Controllers;

[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class AccessibilityRemediationsController
    : Controller
{
    private readonly AccessibilityRemediationService
        _remediationService;

    private readonly AccessibilityRemediationRetestService
        _remediationRetestService;

    private readonly IAuthenticatedAuditService
        _authenticatedAuditService;

    private readonly ApplicationDbContext
        _dbContext;

    public AccessibilityRemediationsController(
        AccessibilityRemediationService remediationService,
        AccessibilityRemediationRetestService remediationRetestService,
        IAuthenticatedAuditService authenticatedAuditService,
        ApplicationDbContext dbContext)
    {
        _remediationService =
            remediationService;

        _remediationRetestService =
            remediationRetestService;

        _authenticatedAuditService =
            authenticatedAuditService;

        _dbContext =
            dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? statusFilter,
        string? applicationFilter,
        string? severityFilter,
        string? assigneeFilter,
        CancellationToken cancellationToken)
    {
        string? normalizedStatusFilter =
            CleanFilterValue(
                statusFilter);

        string? normalizedApplicationFilter =
            CleanFilterValue(
                applicationFilter);

        string? normalizedSeverityFilter =
            CleanFilterValue(
                severityFilter);

        string? normalizedAssigneeFilter =
            CleanFilterValue(
                assigneeFilter);

        List<AccessibilityRemediationItem> remediationItems =
            await _dbContext.AccessibilityRemediationItems
                .AsNoTracking()
                .Include(item =>
                    item.FindingOccurrences)
                    .ThenInclude(occurrence =>
                        occurrence.AuthenticatedAuditFinding)
                        .ThenInclude(finding =>
                            finding.AuthenticatedAuditStep)
                            .ThenInclude(step =>
                                step.AuthenticatedAuditRun)
                .OrderByDescending(item =>
                    item.UpdatedAt)
                .ThenByDescending(item =>
                    item.Id)
                .ToListAsync(
                    cancellationToken);

        AccessibilityRemediationIndexViewModel viewModel =
            new()
            {
                StatusFilter =
                    normalizedStatusFilter,

                ApplicationFilter =
                    normalizedApplicationFilter,

                SeverityFilter =
                    normalizedSeverityFilter,

                AssigneeFilter =
                    normalizedAssigneeFilter,

                /*
                 * Summary cards intentionally represent all currently
                 * tracked remediation work, not only the filtered rows.
                 */
                TotalCount =
                    remediationItems.Count,

                OpenCount =
                    remediationItems.Count(item =>
                        item.Status ==
                        AccessibilityRemediationStatus.Open),

                InProgressCount =
                    remediationItems.Count(item =>
                        item.Status ==
                        AccessibilityRemediationStatus.InProgress),

                FixedCount =
                    remediationItems.Count(item =>
                        item.Status ==
                        AccessibilityRemediationStatus.Fixed),

                VerifiedCount =
                    remediationItems.Count(item =>
                        item.Status ==
                        AccessibilityRemediationStatus.Verified),

                WontFixCount =
                    remediationItems.Count(item =>
                        item.Status ==
                        AccessibilityRemediationStatus.WontFix)
            };

        /*
         * The durable remediation item may accumulate finding
         * occurrences from later retests.
         *
         * The tracker list is classified using the original occurrence.
         */
        foreach (AccessibilityRemediationItem item
            in remediationItems)
        {
            AccessibilityRemediationFindingOccurrence?
                originalOccurrence =
                    item.FindingOccurrences
                        .OrderBy(occurrence =>
                            occurrence.LinkedAt)
                        .ThenBy(occurrence =>
                            occurrence.Id)
                        .FirstOrDefault();

            if (originalOccurrence is null)
            {
                continue;
            }

            AuthenticatedAuditFinding finding =
                originalOccurrence
                    .AuthenticatedAuditFinding;

            AuthenticatedAuditStep step =
                finding.AuthenticatedAuditStep;

            AuthenticatedAuditRun run =
                step.AuthenticatedAuditRun;

            viewModel.Items.Add(
                new AccessibilityRemediationListItemViewModel
                {
                    Id =
                        item.Id,

                    Status =
                        item.Status.ToString(),

                    AssignedTo =
                        item.AssignedTo,

                    CreatedAt =
                        item.CreatedAt,

                    UpdatedAt =
                        item.UpdatedAt,

                    ApplicationName =
                        run.ApplicationName,

                    StepName =
                        step.StepName,

                    Url =
                        step.Url,

                    FindingType =
                        finding.FindingType,

                    RuleId =
                        finding.RuleId,

                    Impact =
                        finding.Impact,

                    WcagLevel =
                        finding.WcagLevel,

                    AffectedElementCount =
                        finding.AffectedElementCount,

                    DetectedAt =
                        step.ScannedAt
                });
        }

        /*
         * Filter choices are built before filtering the result list so
         * selecting one filter does not make the other options disappear.
         */
        viewModel.ApplicationOptions =
            viewModel.Items
                .Select(item =>
                    item.ApplicationName)
                .Where(name =>
                    !string.IsNullOrWhiteSpace(
                        name))
                .Select(name =>
                    name.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(name =>
                    name)
                .ToList();

        viewModel.AssigneeOptions =
            viewModel.Items
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.AssignedTo))
                .Select(item =>
                    item.AssignedTo!.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(name =>
                    name)
                .ToList();

        IEnumerable<AccessibilityRemediationListItemViewModel>
            filteredItems =
                viewModel.Items;

        if (!string.IsNullOrWhiteSpace(
            normalizedStatusFilter))
        {
            filteredItems =
                filteredItems.Where(item =>
                    string.Equals(
                        item.Status,
                        normalizedStatusFilter,
                        StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(
            normalizedApplicationFilter))
        {
            filteredItems =
                filteredItems.Where(item =>
                    string.Equals(
                        item.ApplicationName.Trim(),
                        normalizedApplicationFilter,
                        StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(
            normalizedSeverityFilter))
        {
            filteredItems =
                filteredItems.Where(item =>
                    string.Equals(
                        item.Impact?.Trim(),
                        normalizedSeverityFilter,
                        StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(
            normalizedAssigneeFilter))
        {
            if (string.Equals(
                normalizedAssigneeFilter,
                "Unassigned",
                StringComparison.OrdinalIgnoreCase))
            {
                filteredItems =
                    filteredItems.Where(item =>
                        string.IsNullOrWhiteSpace(
                            item.AssignedTo));
            }
            else
            {
                filteredItems =
                    filteredItems.Where(item =>
                        string.Equals(
                            item.AssignedTo?.Trim(),
                            normalizedAssigneeFilter,
                            StringComparison.OrdinalIgnoreCase));
            }
        }

        viewModel.Items =
            filteredItems
                .OrderByDescending(item =>
                    item.UpdatedAt)
                .ThenByDescending(item =>
                    item.Id)
                .ToList();

        return View(
            viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        int id,
        CancellationToken cancellationToken)
    {
        AccessibilityRemediationItem? remediationItem =
            await _dbContext.AccessibilityRemediationItems
                .AsNoTracking()
                .Include(item =>
                    item.History)
                .Include(item =>
                    item.Retests)
                .Include(item =>
                    item.FindingOccurrences)
                    .ThenInclude(occurrence =>
                        occurrence.AuthenticatedAuditFinding)
                        .ThenInclude(finding =>
                            finding.Nodes)
                .Include(item =>
                    item.FindingOccurrences)
                    .ThenInclude(occurrence =>
                        occurrence.AuthenticatedAuditFinding)
                        .ThenInclude(finding =>
                            finding.AuthenticatedAuditStep)
                            .ThenInclude(step =>
                                step.AuthenticatedAuditRun)
                .FirstOrDefaultAsync(
                    item =>
                        item.Id ==
                        id,
                    cancellationToken);

        if (remediationItem is null)
        {
            return NotFound();
        }

        AccessibilityRemediationFindingOccurrence?
            originalOccurrence =
                remediationItem
                    .FindingOccurrences
                    .OrderBy(occurrence =>
                        occurrence.LinkedAt)
                    .ThenBy(occurrence =>
                        occurrence.Id)
                    .FirstOrDefault();

        if (originalOccurrence is null)
        {
            return NotFound();
        }

        AuthenticatedAuditFinding finding =
            originalOccurrence
                .AuthenticatedAuditFinding;

        AuthenticatedAuditStep step =
            finding.AuthenticatedAuditStep;

        AuthenticatedAuditRun run =
            step.AuthenticatedAuditRun;

        AccessibilityRemediationDetailsViewModel viewModel =
            new()
            {
                Id =
                    remediationItem.Id,

                Status =
                    remediationItem.Status.ToString(),

                AssignedTo =
                    remediationItem.AssignedTo,

                CreatedAt =
                    remediationItem.CreatedAt,

                UpdatedAt =
                    remediationItem.UpdatedAt,

                ApplicationName =
                    run.ApplicationName,

                AuditRunId =
                    run.Id,

                StepNumber =
                    step.StepNumber,

                StepName =
                    step.StepName,

                Url =
                    step.Url,

                PageTitle =
                    step.PageTitle,

                Heading =
                    step.Heading,

                DetectedAt =
                    step.ScannedAt,

                FindingType =
                    finding.FindingType,

                RuleId =
                    finding.RuleId,

                Impact =
                    finding.Impact,

                WcagLevel =
                    finding.WcagLevel,

                WcagTags =
                    finding.WcagTags,

                Help =
                    finding.Help,

                Description =
                    finding.Description,

                HelpUrl =
                    finding.HelpUrl,

                AffectedElementCount =
                    finding.AffectedElementCount,

                Nodes =
                    finding.Nodes
                        .Select(node =>
                            new AccessibilityRemediationNodeViewModel
                            {
                                Target =
                                    node.Target,

                                Html =
                                    node.Html,

                                FailureSummary =
                                    node.FailureSummary,

                                ElementFixGuidance =
                                    node.ElementFixGuidance
                            })
                        .ToList(),

                Retests =
                    remediationItem.Retests
                        .OrderByDescending(retest =>
                            retest.RetestedAt)
                        .ThenByDescending(retest =>
                            retest.Id)
                        .Select(retest =>
                            new AccessibilityRemediationRetestViewModel
                            {
                                Id =
                                    retest.Id,

                                Result =
                                    retest.Result.ToString(),

                                RetestType =
                                    retest.RetestType,

                                AuthenticatedAuditRunId =
                                    retest.AuthenticatedAuditRunId,

                                OriginalAuthenticatedAuditFindingId =
                                    retest
                                        .OriginalAuthenticatedAuditFindingId,

                                MatchMethod =
                                    retest.MatchMethod,

                                MatchConfidence =
                                    retest.MatchConfidence,

                                RetestedAt =
                                    retest.RetestedAt,

                                Notes =
                                    retest.Notes,

                                RetestedBy =
                                    retest.RetestedBy,

                                AuthenticatedAuditStepId =
                                    retest.AuthenticatedAuditStepId,

                                MatchedAuthenticatedAuditFindingId =
                                    retest
                                        .MatchedAuthenticatedAuditFindingId
                            })
                        .ToList(),

                History =
                    remediationItem.History
                        .OrderByDescending(history =>
                            history.ChangedAt)
                        .ThenByDescending(history =>
                            history.Id)
                        .Select(history =>
                            new AccessibilityRemediationHistoryViewModel
                            {
                                EventType =
                                    history.EventType,

                                PreviousStatus =
                                    history
                                        .PreviousStatus?
                                        .ToString(),

                                NewStatus =
                                    history
                                        .NewStatus?
                                        .ToString(),

                                PreviousAssignee =
                                    history.PreviousAssignee,

                                NewAssignee =
                                    history.NewAssignee,

                                Notes =
                                    history.Notes,

                                ChangedAt =
                                    history.ChangedAt,

                                ChangedBy =
                                    history.ChangedBy
                            })
                        .ToList()
            };

        return View(
            viewModel);
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
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] =
                "The remediation item changed while your update " +
                "was being saved. Reload the page and try again.";
        }

        return RedirectToAction(
            nameof(Details),
            new
            {
                id
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(
        int findingId,
        int auditRunId,
        CancellationToken cancellationToken)
    {
        /*
         * Validate the source relationship instead of trusting the hidden
         * auditRunId value from the form.
         */
        bool findingBelongsToAudit =
            await _dbContext.AuthenticatedAuditFindings
                .AsNoTracking()
                .AnyAsync(
                    finding =>
                        finding.Id ==
                            findingId &&
                        finding
                            .AuthenticatedAuditStep
                            .AuthenticatedAuditRunId ==
                            auditRunId,
                    cancellationToken);

        if (!findingBelongsToAudit)
        {
            return NotFound();
        }

        try
        {
            AccessibilityRemediationItem item =
                await _remediationService.CreateForFindingAsync(
                    findingId);

            return RedirectToAction(
                nameof(Details),
                new
                {
                    id =
                        item.Id
                });
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] =
                exception.Message;

            return RedirectToAction(
                "Details",
                "AuthenticatedAudits",
                new
                {
                    id =
                        auditRunId
                });
        }
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
            AuthenticatedAuditSessionResult?
                activeSession =
                    _authenticatedAuditService
                        .GetActiveSession();

            if (activeSession is null)
            {
                TempData["ErrorMessage"] =
                    "No authenticated audit browser session is " +
                    "currently active. Start an authenticated audit, " +
                    "log in, and navigate to the page or workflow " +
                    "state that needs to be retested.";

                return RedirectToAction(
                    nameof(Details),
                    new
                    {
                        id
                    });
            }

            /*
             * Determine the application represented by the original
             * occurrence before scanning anything.
             */
            string? remediationApplicationName =
                await _dbContext
                    .AccessibilityRemediationFindingOccurrences
                    .AsNoTracking()
                    .Where(occurrence =>
                        occurrence
                            .AccessibilityRemediationItemId ==
                        id)
                    .OrderBy(occurrence =>
                        occurrence.LinkedAt)
                    .ThenBy(occurrence =>
                        occurrence.Id)
                    .Select(occurrence =>
                        occurrence
                            .AuthenticatedAuditFinding
                            .AuthenticatedAuditStep
                            .AuthenticatedAuditRun
                            .ApplicationName)
                    .FirstOrDefaultAsync(
                        cancellationToken);

            if (string.IsNullOrWhiteSpace(
                remediationApplicationName))
            {
                throw new InvalidOperationException(
                    "The remediation item's original accessibility " +
                    "finding could not be loaded.");
            }

            /*
             * Avoid scanning and saving an unrelated authenticated state.
             *
             * A current-state retest must use a live browser session for
             * the same application as the tracked remediation item.
             */
            if (!string.Equals(
                remediationApplicationName.Trim(),
                activeSession.ApplicationName.Trim(),
                StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] =
                    $"The active authenticated browser is auditing " +
                    $"'{activeSession.ApplicationName}', but this " +
                    $"remediation item belongs to " +
                    $"'{remediationApplicationName}'. " +
                    "Open an authenticated audit session for the same " +
                    "application before retesting this issue.";

                return RedirectToAction(
                    nameof(Details),
                    new
                    {
                        id
                    });
            }

            /*
             * Reuse the normal authenticated scanner against the current
             * logged-in Playwright state.
             *
             * This creates a normal immutable AuthenticatedAuditStep.
             */
            AuthenticatedAuditStepResult scanResult =
                await _authenticatedAuditService
                    .ScanCurrentStepAsync(
                        activeSession.SessionId,
                        cancellationToken);

            /*
             * ScanCurrentStepAsync returns the public scan result rather
             * than the database primary key.
             *
             * StepNumber is assigned by the live session. If legacy or
             * development data ever contains a duplicate, the highest
             * database id is the newly saved row.
             */
            int? savedStepId =
                await _dbContext.AuthenticatedAuditSteps
                    .AsNoTracking()
                    .Where(step =>
                        step.AuthenticatedAuditRunId ==
                            activeSession.AuditRunId &&
                        step.StepNumber ==
                            scanResult.StepNumber)
                    .OrderByDescending(step =>
                        step.Id)
                    .Select(step =>
                        (int?)step.Id)
                    .FirstOrDefaultAsync(
                        cancellationToken);

            if (!savedStepId.HasValue)
            {
                throw new InvalidOperationException(
                    "The newly scanned authenticated audit step " +
                    "could not be found.");
            }

            AccessibilityRemediationRetest retest =
                await _remediationRetestService
                    .RecordRetestAsync(
                        id,
                        savedStepId.Value,
                        notes,
                        cancellationToken:
                            cancellationToken);

            TempData["SuccessMessage"] =
                retest.Result switch
                {
                    AccessibilityRemediationRetestResult.Detected =>
                        "Retest completed. The tracked accessibility " +
                        "issue is still detected.",

                    AccessibilityRemediationRetestResult.NotDetected =>
                        "Retest completed. The tracked issue was not " +
                        "detected. It remains Fixed – Awaiting " +
                        "Verification until verification is explicitly " +
                        "completed.",

                    AccessibilityRemediationRetestResult.Inconclusive =>
                        "Retest completed, but the result was " +
                        "inconclusive. Confirm that the authenticated " +
                        "browser is on the same page or workflow state.",

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
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] =
                exception.Message;
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] =
                "The remediation item changed while the retest was " +
                "being saved. Reload the page and try again.";
        }
        catch (Exception exception)
        {
            TempData["ErrorMessage"] =
                "The remediation retest could not be completed. " +
                exception.Message;
        }

        return RedirectToAction(
            nameof(Details),
            new
            {
                id
            });
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
                cancellationToken:
                    cancellationToken);

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
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] =
                "The remediation item changed while verification " +
                "was being saved. Reload the page and try again.";
        }
        catch (Exception exception)
        {
            TempData["ErrorMessage"] =
                "The remediation could not be verified. " +
                exception.Message;
        }

        return RedirectToAction(
            nameof(Details),
            new
            {
                id
            });
    }

    private static string? CleanFilterValue(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
            value)
                ? null
                : value.Trim();
    }
}
