using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
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

        List<AccessibilityWorkflowRetestHistoryViewModel>
            formalWorkflowRetests =
            new();

        if (originalAuditRunId.HasValue)
        {
            List<AccessibilityRemediationRetest>
                formalRetestRows =
                    await _dbContext.AccessibilityRemediationRetests
                        .AsNoTracking()
                        .Where(retest =>
                            retest.RetestType == "FullWorkflow" &&
                            retest.AuthenticatedAuditRunId.HasValue &&
                            retest.RemediationItem
                                .FindingOccurrences
                                .Any(occurrence =>
                                    occurrence
                                        .AuthenticatedAuditFinding
                                        .AuthenticatedAuditStep
                                        .AuthenticatedAuditRunId ==
                                    originalAuditRunId.Value))
                        .Include(retest =>
                            retest.AuthenticatedAuditRun)
                        .Include(retest =>
                            retest.RemediationItem)
                        .ToListAsync(cancellationToken);

            List<int> formalRetestItemIds =
                formalRetestRows
                    .Select(retest =>
                    retest.AccessibilityRemediationItemId)
                    .Distinct()
                    .ToList();

            Dictionary<int, int> latestRetestIdsForHistory =
                formalRetestItemIds.Count == 0
                    ? new Dictionary<int, int>()
                    : await _dbContext.AccessibilityRemediationRetests
                .AsNoTracking()
                .Where(retest =>
                    formalRetestItemIds.Contains(
                    retest.AccessibilityRemediationItemId))
                .GroupBy(retest =>
                    retest.AccessibilityRemediationItemId)
                .Select(group =>
                    new
                    {
                    RemediationItemId =
                        group.Key,

                    LatestRetestId =
                        group
                            .OrderByDescending(retest =>
                                retest.RetestedAt)
                            .ThenByDescending(retest =>
                                retest.Id)
                            .Select(retest =>
                                retest.Id)
                            .First()
                    })
                .ToDictionaryAsync(
                    row =>
                    row.RemediationItemId,

                    row =>
                    row.LatestRetestId,

                    cancellationToken);

            List<AccessibilityRemediationHistory> reopenedHistory =
                formalRetestItemIds.Count == 0
                    ? new List<AccessibilityRemediationHistory>()
                    : await _dbContext.AccessibilityRemediationHistories
                        .AsNoTracking()
                        .Where(history =>
                            formalRetestItemIds.Contains(
                                history.AccessibilityRemediationItemId) &&
                            history.EventType == "Reopened")
                        .ToListAsync(cancellationToken);

            formalWorkflowRetests =
                formalRetestRows
                    .GroupBy(retest =>
                        retest.AuthenticatedAuditRunId!.Value)
                    .Select(group =>
                    {
                        AccessibilityRemediationRetest first =
                            group.First();

                        int reopenedCount =
                            group.Count(retest =>
                                reopenedHistory.Any(history =>
                                    history.AccessibilityRemediationItemId ==
                                    retest.AccessibilityRemediationItemId &&
                                history.ChangedAt ==
                                    retest.RetestedAt));

                        int verifiedFromThisRetest =
                            group.Count(retest =>
                                retest.Result ==
                                    AccessibilityRemediationRetestResult.NotDetected &&
                                retest.RemediationItem.Status ==
                                    AccessibilityRemediationStatus.Verified &&
                                latestRetestIdsForHistory.TryGetValue(
                                    retest.AccessibilityRemediationItemId,
                                    out int latestRetestId) &&
                                latestRetestId ==
                                    retest.Id);

                        return new AccessibilityWorkflowRetestHistoryViewModel
                        {
                            RetestAuditRunId =
                                group.Key,

                            ApplicationName =
                                first.AuthenticatedAuditRun?
                                    .ApplicationName ??
                                string.Empty,

                            RetestedAt =
                                group.Max(retest =>
                                    retest.RetestedAt),

                            TotalTracked =
                                group.Count(),

                            StillDetected =
                                group.Count(retest =>
                                    retest.Result ==
                                    AccessibilityRemediationRetestResult
                                        .Detected),

                            Reopened =
                                reopenedCount,

                            NotDetected =
                                group.Count(retest =>
                                    retest.Result ==
                                    AccessibilityRemediationRetestResult
                                        .NotDetected),

                            VerifiedFromThisRetest =
                                verifiedFromThisRetest,

                            Inconclusive =
                                group.Count(retest =>
                                    retest.Result ==
                                    AccessibilityRemediationRetestResult
                                        .Inconclusive),

                            Failed =
                                group.Count(retest =>
                                    retest.Result ==
                                    AccessibilityRemediationRetestResult
                                        .Failed)
                        };
                    })
                    .OrderByDescending(history =>
                        history.RetestedAt)
                    .ToList();
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
                                retest.RetestType == "FullWorkflow" &&
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

                FormalWorkflowRetests =
                    formalWorkflowRetests,


                Comparison =
                    comparison
            });
    }

    [HttpGet]
    public async Task<IActionResult> Details(
    int originalAuditRunId,
    int retestAuditRunId,
    CancellationToken cancellationToken)
    {
        AuthenticatedAuditRun? originalRun =
            await _dbContext.AuthenticatedAuditRuns
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    run =>
                        run.Id == originalAuditRunId,
                    cancellationToken);

        if (originalRun is null)
        {
            return NotFound();
        }

        AuthenticatedAuditRun? retestRun =
            await _dbContext.AuthenticatedAuditRuns
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    run =>
                        run.Id == retestAuditRunId,
                    cancellationToken);

        if (retestRun is null)
        {
            return NotFound();
        }

        List<AccessibilityRemediationRetest> savedRetests =
            await _dbContext.AccessibilityRemediationRetests
                .AsNoTracking()
                .Where(retest =>
                    retest.RetestType == "FullWorkflow" &&
                    retest.AuthenticatedAuditRunId ==
                        retestAuditRunId &&
                    retest.RemediationItem
                        .FindingOccurrences
                        .Any(occurrence =>
                            occurrence
                                .AuthenticatedAuditFinding
                                .AuthenticatedAuditStep
                                .AuthenticatedAuditRunId ==
                            originalAuditRunId))
                .Include(retest =>
                    retest.AuthenticatedAuditStep)

                .Include(retest =>
                    retest.OriginalAuthenticatedAuditFinding)
                    .ThenInclude(finding =>
                        finding.AuthenticatedAuditStep)

                .Include(retest =>
                    retest.RemediationItem)
                    .ThenInclude(item =>
                        item.FindingOccurrences)
                        .ThenInclude(occurrence =>
                            occurrence.AuthenticatedAuditFinding)
                            .ThenInclude(finding =>
                                finding.AuthenticatedAuditStep)
                .ToListAsync(cancellationToken);

        if (savedRetests.Count == 0)
        {
            return NotFound();
        }

        List<int> remediationItemIds =
            savedRetests
                .Select(retest =>
                    retest.AccessibilityRemediationItemId)
                .Distinct()
                .ToList();

        Dictionary<int, int> latestRetestIds =
            await _dbContext.AccessibilityRemediationRetests
        .AsNoTracking()
        .Where(retest =>
            remediationItemIds.Contains(
                retest.AccessibilityRemediationItemId))
        .GroupBy(retest =>
            retest.AccessibilityRemediationItemId)
        .Select(group =>
            new
            {
                RemediationItemId =
                    group.Key,

                LatestRetestId =
                    group
                        .OrderByDescending(retest =>
                            retest.RetestedAt)
                        .ThenByDescending(retest =>
                            retest.Id)
                        .Select(retest =>
                            retest.Id)
                        .First()
            })
        .ToDictionaryAsync(
            row =>
                row.RemediationItemId,

            row =>
                row.LatestRetestId,

            cancellationToken);

        List<AccessibilityRemediationHistory> reopenedHistory =
            await _dbContext.AccessibilityRemediationHistories
                .AsNoTracking()
                .Where(history =>
                    remediationItemIds.Contains(
                        history.AccessibilityRemediationItemId) &&
                    history.EventType == "Reopened")
                .ToListAsync(cancellationToken);

        List<AccessibilityWorkflowRetestSavedItemViewModel>
            items =
                new();

        foreach (AccessibilityRemediationRetest retest
            in savedRetests)
        {
            AuthenticatedAuditFinding? originalFinding =
                retest.OriginalAuthenticatedAuditFinding;

            /*
             * Older formal workflow retests were saved before the exact
             * OriginalAuthenticatedAuditFindingId field existed.
             *
             * Keep a fallback for those historical rows only.
             */
            if (originalFinding is null)
            {
                AccessibilityRemediationFindingOccurrence?
                    originalOccurrence =
                        retest.RemediationItem
                            .FindingOccurrences
                            .Where(occurrence =>
                                occurrence
                                    .AuthenticatedAuditFinding
                                    .AuthenticatedAuditStep
                                    .AuthenticatedAuditRunId ==
                                originalAuditRunId)
                            .OrderBy(occurrence =>
                                occurrence.LinkedAt)
                            .ThenBy(occurrence =>
                                occurrence.Id)
                            .FirstOrDefault();

                originalFinding =
                    originalOccurrence?
                        .AuthenticatedAuditFinding;
            }

            if (originalFinding is null)
            {
                continue;
            }

            AuthenticatedAuditStep originalStep =
                originalFinding.AuthenticatedAuditStep;

            if (originalStep.AuthenticatedAuditRunId !=
                originalAuditRunId)
            {
                throw new InvalidOperationException(
                    $"Formal workflow retest #{retest.Id} references an " +
                    "original finding from a different authenticated audit run.");
            }

            bool wasReopened =
                reopenedHistory.Any(history =>
                    history.AccessibilityRemediationItemId ==
                        retest.AccessibilityRemediationItemId &&
                    history.ChangedAt ==
                        retest.RetestedAt);

            bool canVerify =
                retest.RemediationItem.Status ==
                    AccessibilityRemediationStatus.Fixed &&
                retest.Result ==
                    AccessibilityRemediationRetestResult.NotDetected &&
                latestRetestIds.TryGetValue(
                    retest.AccessibilityRemediationItemId,
                    out int latestRetestId) &&
                latestRetestId ==
                    retest.Id;

            bool verifiedFromThisRetest =
                retest.RemediationItem.Status ==
                    AccessibilityRemediationStatus.Verified &&
                retest.Result ==
                    AccessibilityRemediationRetestResult.NotDetected &&
                latestRetestIds.TryGetValue(
                    retest.AccessibilityRemediationItemId,
                    out int latestVerifiedRetestId) &&
                latestVerifiedRetestId ==
                    retest.Id;

            items.Add(
                new AccessibilityWorkflowRetestSavedItemViewModel
                {
                    RemediationItemId =
                        retest.AccessibilityRemediationItemId,

                    OriginalAuthenticatedAuditFindingId =
                        originalFinding.Id,

                    MatchedAuthenticatedAuditFindingId =
                        retest.MatchedAuthenticatedAuditFindingId,

                    CurrentStatus =
                        retest.RemediationItem.Status,

                    RuleId =
                        originalFinding.RuleId,

                    FindingType =
                        originalFinding.FindingType,

                    Impact =
                        originalFinding.Impact,

                    OriginalStepNumber =
                        originalStep.StepNumber,

                    OriginalStepName =
                        originalStep.StepName,

                    OriginalUrl =
                        originalStep.Url,

                    RetestStepId =
                        retest.AuthenticatedAuditStepId,

                    RetestStepNumber =
                        retest.AuthenticatedAuditStep?
                            .StepNumber,

                    RetestStepName =
                        retest.AuthenticatedAuditStep?
                            .StepName,

                    RetestUrl =
                        retest.AuthenticatedAuditStep?
                            .Url,

                    Result =
                        retest.Result,

                    MatchMethod =
                        retest.MatchMethod,

                    MatchConfidence =
                        retest.MatchConfidence,

                    Notes =
                        retest.Notes,

                    RetestedAt =
                        retest.RetestedAt,

                    CanVerify =
                        canVerify,

                    VerifiedFromThisRetest =
                        verifiedFromThisRetest,

                    WasReopened =
                        wasReopened
                });
        }

        DateTime retestedAt =
            savedRetests.Max(retest =>
                retest.RetestedAt);

        AccessibilityWorkflowRetestDetailsViewModel model =
            new()
            {
                OriginalAuditRunId =
                    originalAuditRunId,

                RetestAuditRunId =
                    retestAuditRunId,

                ApplicationName =
                    retestRun.ApplicationName,

                RetestedAt =
                    retestedAt,

                TotalTracked =
                    items.Count,

                StillDetected =
                    items.Count(item =>
                        item.Result ==
                        AccessibilityRemediationRetestResult.Detected),

                NotDetected =
                    items.Count(item =>
                        item.Result ==
                        AccessibilityRemediationRetestResult.NotDetected),

                VerifiedFromThisRetest =
                    items.Count(item =>
                        item.VerifiedFromThisRetest),

                Inconclusive =
                    items.Count(item =>
                        item.Result ==
                        AccessibilityRemediationRetestResult.Inconclusive),

                Failed =
                    items.Count(item =>
                        item.Result ==
                        AccessibilityRemediationRetestResult.Failed),

                Reopened =
                    items.Count(item =>
                        item.WasReopened),

                Items =
                    items
                        .OrderBy(item =>
                            item.Result ==
                            AccessibilityRemediationRetestResult.Detected
                                ? 0
                                : item.Result ==
                                  AccessibilityRemediationRetestResult.Failed
                                    ? 1
                                    : item.Result ==
                                      AccessibilityRemediationRetestResult.Inconclusive
                                        ? 2
                                        : 3)
                        .ThenBy(item =>
                            item.OriginalStepNumber)
                        .ThenBy(item =>
                            item.RuleId)
                        .ToList()
            };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifySelected(
    int originalAuditRunId,
    int retestAuditRunId,
    List<int>? remediationItemIds,
    string? notes,
    CancellationToken cancellationToken)
    {
        try
        {
            AccessibilityWorkflowVerificationResult result =
                await _retestService.VerifyWorkflowItemsAsync(
                    retestAuditRunId,
                    remediationItemIds ??
                        new List<int>(),
                    notes,
                    cancellationToken: cancellationToken);

            TempData["WorkflowVerificationSuccess"] =
                $"{result.Verified} remediation issue(s) were verified.";
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            TempData["WorkflowVerificationError"] =
                exception.Message;
        }
        catch (Exception exception)
        {
            TempData["WorkflowVerificationError"] =
                "The selected remediation issues could not be verified. " +
                exception.Message;
        }

        return RedirectToAction(
            nameof(Details),
            new
            {
                originalAuditRunId,
                retestAuditRunId
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
