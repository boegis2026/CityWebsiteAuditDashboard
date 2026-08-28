using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Services.Remediation;
using CityWebsiteAuditDashboard.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Controllers;

[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class AccessibilityWorkflowRetestsController
    : Controller
{
    private readonly ApplicationDbContext _dbContext;

    private readonly AccessibilityRemediationWorkflowComparisonService
        _comparisonService;

    private readonly AccessibilityRemediationRetestService
        _retestService;

    public AccessibilityWorkflowRetestsController(
        ApplicationDbContext dbContext,
        AccessibilityRemediationWorkflowComparisonService
            comparisonService,
        AccessibilityRemediationRetestService
            retestService)
    {
        _dbContext = dbContext;
        _comparisonService = comparisonService;
        _retestService = retestService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int? originalAuditRunId,
        int? retestAuditRunId,
        CancellationToken cancellationToken)
    {
        List<AccessibilityWorkflowRetestRunOptionViewModel>
            auditRuns =
                await _dbContext.AuthenticatedAuditRuns
                    .AsNoTracking()
                    .Where(run =>
                        run.Steps.Any())
                    .OrderByDescending(run =>
                        run.StartedAt)
                    .ThenByDescending(run =>
                        run.Id)
                    .Select(run =>
                        new AccessibilityWorkflowRetestRunOptionViewModel
                        {
                            Id =
                                run.Id,

                            ApplicationName =
                                run.ApplicationName,

                            StartedAt =
                                run.StartedAt,

                            Status =
                                run.Status,

                            StepCount =
                                run.Steps.Count
                        })
                    .ToListAsync(cancellationToken);

        List<AccessibilityWorkflowRetestRunOptionViewModel>
            eligibleRetestRuns =
            new();

        if (originalAuditRunId.HasValue)
        {
            AccessibilityWorkflowRetestRunOptionViewModel?
                originalRun =
                    auditRuns.FirstOrDefault(run =>
                        run.Id ==
                        originalAuditRunId.Value);

            if (originalRun is not null)
            {
                eligibleRetestRuns =
                    auditRuns
                        .Where(run =>
                            run.Id != originalRun.Id &&
                            string.Equals(
                                run.ApplicationName,
                                originalRun.ApplicationName,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                run.Status,
                                "Completed",
                                StringComparison.OrdinalIgnoreCase) &&
                            run.StartedAt >
                                originalRun.StartedAt)
                        .OrderByDescending(run =>
                            run.StartedAt)
                        .ThenByDescending(run =>
                            run.Id)
                        .ToList();
            }
        }

        AccessibilityWorkflowRetestComparisonViewModel?
            comparison = null;

        if (originalAuditRunId.HasValue &&
            retestAuditRunId.HasValue)
        {
            try
            {
                AccessibilityRemediationWorkflowComparisonResult
                    comparisonResult =
                        await _comparisonService.CompareAsync(
                            originalAuditRunId.Value,
                            retestAuditRunId.Value,
                            cancellationToken);

                List<int> comparedRemediationItemIds =
                    comparisonResult.Items
                        .Select(item =>
                            item.RemediationItemId)
                        .Distinct()
                        .ToList();

                bool formalRetestAlreadyApplied =
                    comparedRemediationItemIds.Count > 0 &&
                    await _dbContext.AccessibilityRemediationRetests
                        .AsNoTracking()
                        .AnyAsync(
                            retest =>
                                retest.AuthenticatedAuditRunId ==
                                    retestAuditRunId.Value &&
                                comparedRemediationItemIds.Contains(
                                    retest.AccessibilityRemediationItemId),
                            cancellationToken);

                comparison =
                    new AccessibilityWorkflowRetestComparisonViewModel
                    {
                        OriginalAuditRunId =
                            comparisonResult.OriginalAuditRunId,

                        RetestAuditRunId =
                            comparisonResult.RetestAuditRunId,

                        ApplicationName =
                            comparisonResult.ApplicationName,

                        FormalRetestAlreadyApplied =
                            formalRetestAlreadyApplied,

                        OriginalAuditStartedAt =
                            comparisonResult.OriginalAuditStartedAt,

                        RetestAuditStartedAt =
                            comparisonResult.RetestAuditStartedAt,

                        RetestAuditStatus =
                            comparisonResult.RetestAuditStatus,

                        TotalTracked =
                            comparisonResult.TotalTracked,

                        StillDetected =
                            comparisonResult.StillDetected,

                        NotDetected =
                            comparisonResult.NotDetected,

                        Inconclusive =
                            comparisonResult.Inconclusive,

                        Failed =
                            comparisonResult.Failed,

                        Items =
                            comparisonResult.Items
                                .Select(item =>
                                    new AccessibilityWorkflowRetestComparisonItemViewModel
                                    {
                                        RemediationItemId =
                                            item.RemediationItemId,

                                        CurrentStatus =
                                            item.CurrentStatus,

                                        RuleId =
                                            item.RuleId,

                                        FindingType =
                                            item.FindingType,

                                        Impact =
                                            item.Impact,

                                        OriginalStepNumber =
                                            item.OriginalStepNumber,

                                        OriginalStepName =
                                            item.OriginalStepName,

                                        OriginalUrl =
                                            item.OriginalUrl,

                                        RetestStepNumber =
                                            item.RetestStepNumber,

                                        RetestStepName =
                                            item.RetestStepName,

                                        RetestUrl =
                                            item.RetestUrl,

                                        StateConfidence =
                                            item.StateConfidence,

                                        Result =
                                            item.Result,

                                        MatchMethod =
                                            item.MatchMethod,

                                        MatchConfidence =
                                            item.MatchConfidence,

                                        Message =
                                            item.Message
                                    })
                                .ToList()
                    };
            }
            catch (InvalidOperationException exception)
            {
                ModelState.AddModelError(
                    string.Empty,
                    exception.Message);
            }
        }

        return View(
            new AccessibilityWorkflowRetestViewModel
            {
                OriginalAuditRunId =
                    originalAuditRunId,

                RetestAuditRunId =
                    retestAuditRunId,

                AuditRuns =
                    auditRuns,

                EligibleRetestRuns =
                    eligibleRetestRuns,

                Comparison =
                    comparison
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(
    int originalAuditRunId,
    int retestAuditRunId,
    string? notes,
    CancellationToken cancellationToken)
    {
        try
        {
            AccessibilityWorkflowRetestApplyResult result =
                await _retestService.ApplyWorkflowRetestAsync(
                    originalAuditRunId,
                    retestAuditRunId,
                    notes,
                    cancellationToken: cancellationToken);

            TempData["WorkflowRetestSuccess"] =
                $"Formal workflow retest saved. " +
                $"{result.TotalTracked} tracked issue(s) were retested: " +
                $"{result.StillDetected} still detected, " +
                $"{result.NotDetected} not detected, " +
                $"{result.Inconclusive} inconclusive, " +
                $"{result.Failed} failed. " +
                $"{result.Reopened} issue(s) were reopened.";
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            TempData["WorkflowRetestError"] =
                exception.Message;
        }
        catch (Exception exception)
        {
            TempData["WorkflowRetestError"] =
                "The formal workflow retest could not be saved. " +
                exception.Message;
        }

        return RedirectToAction(
            nameof(Index),
            new
            {
                originalAuditRunId,
                retestAuditRunId
            });
    }
}
