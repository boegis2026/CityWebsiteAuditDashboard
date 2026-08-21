using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using CityWebsiteAuditDashboard.Services.Remediation;
using CityWebsiteAuditDashboard.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Controllers;

public sealed class AccessibilityRemediationsController : Controller
{
    private readonly AccessibilityRemediationService _remediationService;
    private readonly ApplicationDbContext _dbContext;

    public AccessibilityRemediationsController(
    AccessibilityRemediationService remediationService,
    ApplicationDbContext dbContext)
    {
        _remediationService = remediationService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
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

        AccessibilityRemediationIndexViewModel viewModel = new();

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

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var remediationItem =
            await _dbContext.AccessibilityRemediationItems
                .AsNoTracking()
                .Include(item => item.History)
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
}
