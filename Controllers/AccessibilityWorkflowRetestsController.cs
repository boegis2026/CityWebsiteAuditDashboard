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

        AccessibilityWorkflowRetestRunOptionViewModel?
            selectedOriginalRun =
                null;

        List<AccessibilityWorkflowRetestRunOptionViewModel>
            eligibleRetestRuns =
                new();

        if (originalAuditRunId.HasValue)
        {
            selectedOriginalRun =
                auditRuns.FirstOrDefault(run =>
                    run.Id ==
                    originalAuditRunId.Value);

            if (selectedOriginalRun is not null)
            {
                eligibleRetestRuns =
                    auditRuns
                        .Where(run =>
                            run.Id !=
                                selectedOriginalRun.Id &&
                            string.Equals(
                                run.ApplicationName.Trim(),
                                selectedOriginalRun
                                    .ApplicationName
                                    .Trim(),
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                run.Status,
                                "Completed",
                                StringComparison.OrdinalIgnoreCase) &&
                            run.StartedAt >
                                selectedOriginalRun.StartedAt)
                        .OrderByDescending(run =>
                            run.StartedAt)
                        .ThenByDescending(run =>
                            run.Id)
                        .ToList();
            }
            else
            {
                ModelState.AddModelError(
                    string.Empty,
                    "The selected original authenticated audit could not be found.");
            }
        }

        /*
         * If somebody manually supplies an incompatible retest id in
         * the query string, do not silently treat it as valid.
         */
        if (retestAuditRunId.HasValue &&
            selectedOriginalRun is not null &&
            !eligibleRetestRuns.Any(run =>
                run.Id ==
                retestAuditRunId.Value))
        {
            ModelState.AddModelError(
                string.Empty,
                "The selected retest audit is not a compatible completed " +
                "audit for the selected original run.");

            retestAuditRunId =
                null;
        }

        List<AccessibilityWorkflowRetestHistoryViewModel>
            formalWorkflowRetests =
                new();

        if (originalAuditRunId.HasValue &&
            selectedOriginalRun is not null)
        {
            /*
             * New formal retests contain
             * OriginalAuthenticatedAuditFindingId.
             *
             * Use that exact source whenever it exists.
             *
             * The second branch exists only for older development rows
             * that were saved before the source-finding FK was added.
             */
            List<AccessibilityRemediationRetest>
                formalRetestRows =
                    await _dbContext.AccessibilityRemediationRetests
                        .AsNoTracking()
                        .Where(retest =>
                            retest.RetestType ==
                                "FullWorkflow" &&
                            retest.AuthenticatedAuditRunId.HasValue &&
                            (
                                (
                                    retest
                                        .OriginalAuthenticatedAuditFindingId
                                        .HasValue &&
                                    retest
                                        .OriginalAuthenticatedAuditFinding!
                                        .AuthenticatedAuditStep
                                        .AuthenticatedAuditRunId ==
                                    originalAuditRunId.Value
                                )
                                ||
                                (
                                    !retest
                                        .OriginalAuthenticatedAuditFindingId
                                        .HasValue &&
                                    retest
                                        .RemediationItem
                                        .FindingOccurrences
                                        .Any(occurrence =>
                                            occurrence
                                                .AuthenticatedAuditFinding
                                                .AuthenticatedAuditStep
                                                .AuthenticatedAuditRunId ==
                                            originalAuditRunId.Value)
                                )
                            ))
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

            Dictionary<int, int>
                latestRetestIdsForHistory =
                    await LoadLatestRetestIdsAsync(
                        formalRetestItemIds,
                        cancellationToken);

            List<AccessibilityRemediationHistory>
                reopenedHistory =
                    formalRetestItemIds.Count == 0
                        ? new List<
                            AccessibilityRemediationHistory>()
                        : await _dbContext
                            .AccessibilityRemediationHistories
                            .AsNoTracking()
                            .Where(history =>
                                formalRetestItemIds.Contains(
                                    history
                                        .AccessibilityRemediationItemId) &&
                                history.EventType ==
                                    "Reopened")
                            .ToListAsync(cancellationToken);

            formalWorkflowRetests =
                formalRetestRows
                    .GroupBy(retest =>
                        retest
                            .AuthenticatedAuditRunId!
                            .Value)
                    .Select(group =>
                    {
                        AccessibilityRemediationRetest
                            first =
                                group.First();

                        int reopenedCount =
                            group.Count(retest =>
                                reopenedHistory.Any(history =>
                                    history
                                        .AccessibilityRemediationItemId ==
                                    retest
                                        .AccessibilityRemediationItemId &&
                                    history.ChangedAt ==
                                    retest.RetestedAt));

                        int verifiedFromThisRetest =
                            group.Count(retest =>
                                retest.Result ==
                                    AccessibilityRemediationRetestResult
                                        .NotDetected &&
                                retest.RemediationItem.Status ==
                                    AccessibilityRemediationStatus
                                        .Verified &&
                                latestRetestIdsForHistory
                                    .TryGetValue(
                                        retest
                                            .AccessibilityRemediationItemId,
                                        out int latestRetestId) &&
                                latestRetestId ==
                                    retest.Id);

                        return new
                            AccessibilityWorkflowRetestHistoryViewModel
                        {
                            RetestAuditRunId =
                                    group.Key,

                            ApplicationName =
                                    first
                                        .AuthenticatedAuditRun?
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
                                            .Failed),

                            Reopened =
                                    reopenedCount
                        };
                    })
                    .OrderByDescending(history =>
                        history.RetestedAt)
                    .ThenByDescending(history =>
                        history.RetestAuditRunId)
                    .ToList();
        }

        AccessibilityWorkflowRetestComparisonViewModel?
            comparison =
                null;

        if (originalAuditRunId.HasValue &&
            retestAuditRunId.HasValue &&
            selectedOriginalRun is not null)
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

                /*
                 * A retest audit run should not be applied repeatedly to
                 * the same durable remediation items.
                 *
                 * This intentionally matches the service-level duplicate
                 * protection.
                 */
                bool formalRetestAlreadyApplied =
                    comparedRemediationItemIds.Count > 0 &&
                    await _dbContext
                        .AccessibilityRemediationRetests
                        .AsNoTracking()
                        .AnyAsync(
                            retest =>
                                retest.RetestType ==
                                    "FullWorkflow" &&
                                retest.AuthenticatedAuditRunId ==
                                    retestAuditRunId.Value &&
                                comparedRemediationItemIds.Contains(
                                    retest
                                        .AccessibilityRemediationItemId),
                            cancellationToken);

                comparison =
                    new AccessibilityWorkflowRetestComparisonViewModel
                    {
                        OriginalAuditRunId =
                            comparisonResult
                                .OriginalAuditRunId,

                        RetestAuditRunId =
                            comparisonResult
                                .RetestAuditRunId,

                        ApplicationName =
                            comparisonResult
                                .ApplicationName,

                        FormalRetestAlreadyApplied =
                            formalRetestAlreadyApplied,

                        OriginalAuditStartedAt =
                            comparisonResult
                                .OriginalAuditStartedAt,

                        RetestAuditStartedAt =
                            comparisonResult
                                .RetestAuditStartedAt,

                        RetestAuditStatus =
                            comparisonResult
                                .RetestAuditStatus,

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
                                    new
                                        AccessibilityWorkflowRetestComparisonItemViewModel
                                    {
                                        RemediationItemId =
                                                item
                                                    .RemediationItemId,

                                        CurrentStatus =
                                                item
                                                    .CurrentStatus,

                                        RuleId =
                                                item.RuleId,

                                        FindingType =
                                                item.FindingType,

                                        Impact =
                                                item.Impact,

                                        OriginalStepNumber =
                                                item
                                                    .OriginalStepNumber,

                                        OriginalStepName =
                                                item
                                                    .OriginalStepName,

                                        OriginalUrl =
                                                item
                                                    .OriginalUrl,

                                        RetestStepNumber =
                                                item
                                                    .RetestStepNumber,

                                        RetestStepName =
                                                item
                                                    .RetestStepName,

                                        RetestUrl =
                                                item.RetestUrl,

                                        StateConfidence =
                                                item
                                                    .StateConfidence,

                                        Result =
                                                item.Result,

                                        MatchMethod =
                                                item.MatchMethod,

                                        MatchConfidence =
                                                item
                                                    .MatchConfidence,

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

        AccessibilityWorkflowRetestViewModel model =
            new()
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
            };

        return View(model);
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
                        run.Id ==
                        originalAuditRunId,
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
                        run.Id ==
                        retestAuditRunId,
                    cancellationToken);

        if (retestRun is null)
        {
            return NotFound();
        }

        if (!string.Equals(
                originalRun.ApplicationName.Trim(),
                retestRun.ApplicationName.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(
                "The original and retest audit runs belong to " +
                "different applications.");
        }

        /*
         * Exact-source rows:
         *     OriginalAuthenticatedAuditFindingId identifies the exact
         *     original audit finding.
         *
         * Legacy rows:
         *     Fall back to occurrence membership because old records
         *     did not save the source finding id.
         */
        List<AccessibilityRemediationRetest> savedRetests =
            await _dbContext.AccessibilityRemediationRetests
                .AsNoTracking()
                .Where(retest =>
                    retest.RetestType ==
                        "FullWorkflow" &&
                    retest.AuthenticatedAuditRunId ==
                        retestAuditRunId &&
                    (
                        (
                            retest
                                .OriginalAuthenticatedAuditFindingId
                                .HasValue &&
                            retest
                                .OriginalAuthenticatedAuditFinding!
                                .AuthenticatedAuditStep
                                .AuthenticatedAuditRunId ==
                            originalAuditRunId
                        )
                        ||
                        (
                            !retest
                                .OriginalAuthenticatedAuditFindingId
                                .HasValue &&
                            retest
                                .RemediationItem
                                .FindingOccurrences
                                .Any(occurrence =>
                                    occurrence
                                        .AuthenticatedAuditFinding
                                        .AuthenticatedAuditStep
                                        .AuthenticatedAuditRunId ==
                                    originalAuditRunId)
                        )
                    ))
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
            await LoadLatestRetestIdsAsync(
                remediationItemIds,
                cancellationToken);

        List<AccessibilityRemediationHistory>
            reopenedHistory =
                remediationItemIds.Count == 0
                    ? new List<
                        AccessibilityRemediationHistory>()
                    : await _dbContext
                        .AccessibilityRemediationHistories
                        .AsNoTracking()
                        .Where(history =>
                            remediationItemIds.Contains(
                                history
                                    .AccessibilityRemediationItemId) &&
                            history.EventType ==
                                "Reopened")
                        .ToListAsync(cancellationToken);

        List<AccessibilityWorkflowRetestSavedItemViewModel>
            items =
                new();

        foreach (AccessibilityRemediationRetest retest
            in savedRetests)
        {
            AuthenticatedAuditFinding? originalFinding =
                ResolveOriginalFinding(
                    retest,
                    originalAuditRunId);

            if (originalFinding is null)
            {
                /*
                 * A corrupt or incomplete legacy row should not make
                 * the entire historical details page unusable.
                 */
                continue;
            }

            AuthenticatedAuditStep originalStep =
                originalFinding
                    .AuthenticatedAuditStep;

            /*
             * Modern rows should never reach this state because the
             * database query above already scopes the exact source.
             * Keep this guard as an integrity check.
             */
            if (originalStep.AuthenticatedAuditRunId !=
                originalAuditRunId)
            {
                continue;
            }

            bool wasReopened =
                reopenedHistory.Any(history =>
                    history
                        .AccessibilityRemediationItemId ==
                    retest
                        .AccessibilityRemediationItemId &&
                    history.ChangedAt ==
                    retest.RetestedAt);

            bool isLatestEvidence =
                latestRetestIds.TryGetValue(
                    retest
                        .AccessibilityRemediationItemId,
                    out int latestRetestId) &&
                latestRetestId ==
                    retest.Id;

            bool canVerify =
                retest.RemediationItem.Status ==
                    AccessibilityRemediationStatus.Fixed &&
                retest.Result ==
                    AccessibilityRemediationRetestResult
                        .NotDetected &&
                isLatestEvidence;

            bool verifiedFromThisRetest =
                retest.RemediationItem.Status ==
                    AccessibilityRemediationStatus.Verified &&
                retest.Result ==
                    AccessibilityRemediationRetestResult
                        .NotDetected &&
                isLatestEvidence;

            items.Add(
                new AccessibilityWorkflowRetestSavedItemViewModel
                {
                    RemediationItemId =
                        retest
                            .AccessibilityRemediationItemId,

                    OriginalAuthenticatedAuditFindingId =
                        originalFinding.Id,

                    MatchedAuthenticatedAuditFindingId =
                        retest
                            .MatchedAuthenticatedAuditFindingId,

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
                        retest
                            .AuthenticatedAuditStepId,

                    RetestStepNumber =
                        retest
                            .AuthenticatedAuditStep?
                            .StepNumber,

                    RetestStepName =
                        retest
                            .AuthenticatedAuditStep?
                            .StepName,

                    RetestUrl =
                        retest
                            .AuthenticatedAuditStep?
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

        if (items.Count == 0)
        {
            return NotFound();
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
                        AccessibilityRemediationRetestResult
                            .Detected),

                NotDetected =
                    items.Count(item =>
                        item.Result ==
                        AccessibilityRemediationRetestResult
                            .NotDetected),

                VerifiedFromThisRetest =
                    items.Count(item =>
                        item.VerifiedFromThisRetest),

                Inconclusive =
                    items.Count(item =>
                        item.Result ==
                        AccessibilityRemediationRetestResult
                            .Inconclusive),

                Failed =
                    items.Count(item =>
                        item.Result ==
                        AccessibilityRemediationRetestResult
                            .Failed),

                Reopened =
                    items.Count(item =>
                        item.WasReopened),

                Items =
                    items
                        .OrderBy(item =>
                            GetResultOrder(
                                item.Result))
                        .ThenBy(item =>
                            item.OriginalStepNumber)
                        .ThenBy(item =>
                            item.RuleId)
                        .ThenBy(item =>
                            item.RemediationItemId)
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
                    cancellationToken:
                        cancellationToken);

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
                    cancellationToken:
                        cancellationToken);

            TempData["WorkflowRetestSuccess"] =
                "Formal workflow retest saved. " +
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

    /// <summary>
    /// Returns the newest saved retest id for each requested durable
    /// remediation item.
    ///
    /// Current-state and full-workflow retests are both included because
    /// either one can supersede older verification evidence.
    /// </summary>
    private async Task<Dictionary<int, int>>
        LoadLatestRetestIdsAsync(
            IReadOnlyCollection<int> remediationItemIds,
            CancellationToken cancellationToken)
    {
        if (remediationItemIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        List<int> itemIds =
            remediationItemIds
                .Distinct()
                .ToList();

        /*
         * Materialize before grouping.
         *
         * This avoids depending on provider-specific translation of a
         * GroupBy + ordered First expression.
         */
        var retestRows =
            await _dbContext.AccessibilityRemediationRetests
                .AsNoTracking()
                .Where(retest =>
                    itemIds.Contains(
                        retest.AccessibilityRemediationItemId))
                .Select(retest =>
                    new
                    {
                        retest.Id,
                        retest.AccessibilityRemediationItemId,
                        retest.RetestedAt
                    })
                .ToListAsync(cancellationToken);

        return retestRows
            .GroupBy(retest =>
                retest.AccessibilityRemediationItemId)
            .ToDictionary(
                group =>
                    group.Key,

                group =>
                    group
                        .OrderByDescending(retest =>
                            retest.RetestedAt)
                        .ThenByDescending(retest =>
                            retest.Id)
                        .First()
                        .Id);
    }

    /// <summary>
    /// Resolves the source finding represented by one saved formal
    /// workflow retest row.
    ///
    /// New rows use the exact saved source FK.
    /// Older rows fall back to the earliest occurrence from the original
    /// audit run supplied by the historical route.
    /// </summary>
    private static AuthenticatedAuditFinding?
        ResolveOriginalFinding(
            AccessibilityRemediationRetest retest,
            int originalAuditRunId)
    {
        if (retest.OriginalAuthenticatedAuditFinding is not null)
        {
            return retest
                .OriginalAuthenticatedAuditFinding
                .AuthenticatedAuditStep
                .AuthenticatedAuditRunId ==
                originalAuditRunId
                    ? retest
                        .OriginalAuthenticatedAuditFinding
                    : null;
        }

        AccessibilityRemediationFindingOccurrence?
            legacyOccurrence =
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

        return legacyOccurrence?
            .AuthenticatedAuditFinding;
    }

    private static int GetResultOrder(
        AccessibilityRemediationRetestResult result)
    {
        return result switch
        {
            AccessibilityRemediationRetestResult.Detected =>
                0,

            AccessibilityRemediationRetestResult.Failed =>
                1,

            AccessibilityRemediationRetestResult.Inconclusive =>
                2,

            AccessibilityRemediationRetestResult.NotDetected =>
                3,

            _ =>
                4
        };
    }
}
