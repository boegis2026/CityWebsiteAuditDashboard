using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using CityWebsiteAuditDashboard.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Controllers;

/// <summary>
/// Builds the read-only, management-facing accessibility reporting dashboard
/// from saved authenticated axe-core audit results and remediation evidence.
/// </summary>
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class AccessibilityOverviewController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public AccessibilityOverviewController(
        ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? applicationName,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? severity = null,
        string? wcagLevel = null,
        string? findingType = null,
        bool latestOnly = true,
        CancellationToken cancellationToken = default)
    {
        string? normalizedApplicationName =
            NormalizeOptionalValue(applicationName);

        string? normalizedSeverity =
            NormalizeSeverity(severity);

        string? normalizedWcagLevel =
            NormalizeWcagLevel(wcagLevel);

        string? normalizedFindingType =
            NormalizeFindingType(findingType);

        DateTime? normalizedStartDate =
            startDate?.Date;

        DateTime? normalizedEndDate =
            endDate?.Date;

        DateTime? endDateExclusive =
            normalizedEndDate?.AddDays(1);

        if (normalizedStartDate.HasValue &&
            normalizedEndDate.HasValue &&
            normalizedStartDate.Value >
            normalizedEndDate.Value)
        {
            ModelState.AddModelError(
                nameof(startDate),
                "The start date cannot be after the end date.");
        }

        List<string> applicationOptions =
            await _dbContext.AuthenticatedAuditRuns
                .AsNoTracking()
                .Select(run =>
                    run.ApplicationName)
                .Distinct()
                .OrderBy(name =>
                    name)
                .ToListAsync(cancellationToken);

        List<AuthenticatedRunSnapshot> authenticatedRuns =
            await LoadAuthenticatedRunsAsync(
                normalizedApplicationName,
                normalizedStartDate,
                endDateExclusive,
                cancellationToken);

        List<AuthenticatedRunSnapshot> selectedAuthenticatedRuns =
            SelectAuthenticatedRuns(
                authenticatedRuns,
                latestOnly);

        List<int> selectedRunIds =
            selectedAuthenticatedRuns
                .Select(run =>
                    run.Id)
                .ToList();

        List<AuthenticatedStepSnapshot> authenticatedSteps =
            await LoadAuthenticatedStepsAsync(
                selectedRunIds,
                cancellationToken);

        List<AuthenticatedStepSnapshot> successfulAuthenticatedSteps =
            authenticatedSteps
                .Where(step =>
                    step.ScanSucceeded)
                .ToList();

        List<int> successfulStepIds =
            successfulAuthenticatedSteps
                .Select(step =>
                    step.Id)
                .ToList();

        List<AuthenticatedFindingSnapshot> authenticatedFindings =
            await LoadAuthenticatedFindingsAsync(
                successfulStepIds,
                cancellationToken);

        List<AuthenticatedFindingSnapshot> filteredFindings =
            ApplyFindingFilters(
                authenticatedFindings,
                normalizedSeverity,
                normalizedWcagLevel,
                normalizedFindingType);

        AccessibilityOverviewSummaryViewModel summary =
            BuildSummary(
                selectedAuthenticatedRuns,
                successfulAuthenticatedSteps,
                filteredFindings);

        AccessibilityHealthViewModel health =
            BuildHealth(
                successfulAuthenticatedSteps,
                filteredFindings);

        AccessibilityIssueBreakdownViewModel issueBreakdown =
            BuildIssueBreakdown(
                filteredFindings);

        AccessibilityRemediationProgressViewModel remediationProgress =
            await LoadRemediationProgressAsync(
                normalizedApplicationName,
                normalizedStartDate,
                endDateExclusive,
                normalizedSeverity,
                normalizedWcagLevel,
                normalizedFindingType,
                cancellationToken);

        /*
         * The ranking table needs both the latest and previous run
         * for each application so management can see directional change.
         */
        List<AuthenticatedRunSnapshot> comparisonAuthenticatedRuns =
            SelectLatestAndPreviousAuthenticatedRuns(
                authenticatedRuns);

        List<int> comparisonRunIds =
            comparisonAuthenticatedRuns
                .Select(run =>
                    run.Id)
                .ToList();

        List<AuthenticatedStepSnapshot> comparisonAuthenticatedSteps =
            await LoadAuthenticatedStepsAsync(
                comparisonRunIds,
                cancellationToken);

        List<int> successfulComparisonStepIds =
            comparisonAuthenticatedSteps
                .Where(step =>
                    step.ScanSucceeded)
                .Select(step =>
                    step.Id)
                .ToList();

        List<AuthenticatedFindingSnapshot> comparisonAuthenticatedFindings =
            await LoadAuthenticatedFindingsAsync(
                successfulComparisonStepIds,
                cancellationToken);

        List<AuthenticatedFindingSnapshot> filteredComparisonFindings =
            ApplyFindingFilters(
                comparisonAuthenticatedFindings,
                normalizedSeverity,
                normalizedWcagLevel,
                normalizedFindingType);

        List<AccessibilityApplicationRankingViewModel>
            applicationRankings =
                BuildApplicationRankings(
                    comparisonAuthenticatedRuns,
                    comparisonAuthenticatedSteps,
                    filteredComparisonFindings);

        List<AccessibilityTopFindingViewModel> topFindings =
            BuildTopFindings(
                selectedAuthenticatedRuns,
                successfulAuthenticatedSteps,
                filteredFindings);

        List<AccessibilityTrendPointViewModel> trendPoints =
            BuildTrendPoints(
                successfulAuthenticatedSteps,
                filteredFindings);

        AccessibilityOverviewViewModel model =
            new()
            {
                Filters =
                    new AccessibilityOverviewFilterViewModel
                    {
                        ApplicationName =
                            normalizedApplicationName,

                        StartDate =
                            normalizedStartDate,

                        EndDate =
                            normalizedEndDate,

                        Severity =
                            normalizedSeverity,

                        WcagLevel =
                            normalizedWcagLevel,

                        FindingType =
                            normalizedFindingType,

                        LatestOnly =
                            latestOnly
                    },

                ApplicationOptions =
                    applicationOptions,

                Summary =
                    summary,

                Health =
                    health,

                RemediationProgress =
                    remediationProgress,

                IssueBreakdown =
                    issueBreakdown,

                Applications =
                    applicationRankings,

                TopFindings =
                    topFindings,

                Trends =
                    trendPoints
            };

        return View(model);
    }

    private async Task<AccessibilityRemediationProgressViewModel>
        LoadRemediationProgressAsync(
            string? applicationName,
            DateTime? startDate,
            DateTime? endDateExclusive,
            string? severity,
            string? wcagLevel,
            string? findingType,
            CancellationToken cancellationToken)
    {
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
                .ToListAsync(cancellationToken);

        List<AccessibilityRemediationItem> filteredItems =
            new();

        /*
         * A remediation item is durable and can accumulate later
         * finding occurrences.
         *
         * Reporting classification is based on its original occurrence.
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

            if (!string.IsNullOrWhiteSpace(applicationName) &&
                !string.Equals(
                    run.ApplicationName.Trim(),
                    applicationName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (startDate.HasValue &&
                step.ScannedAt <
                startDate.Value)
            {
                continue;
            }

            if (endDateExclusive.HasValue &&
                step.ScannedAt >=
                endDateExclusive.Value)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(severity))
            {
                if (severity == "Unknown")
                {
                    if (IsKnownImpact(
                        finding.Impact))
                    {
                        continue;
                    }
                }
                else if (!string.Equals(
                    finding.Impact?.Trim(),
                    severity,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(wcagLevel))
            {
                if (wcagLevel == "Unmapped")
                {
                    if (IsWcagLevel(
                            finding.WcagLevel,
                            "A") ||
                        IsWcagLevel(
                            finding.WcagLevel,
                            "AA"))
                    {
                        continue;
                    }
                }
                else if (!IsWcagLevel(
                    finding.WcagLevel,
                    wcagLevel))
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(findingType) &&
                !string.Equals(
                    finding.FindingType,
                    findingType,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filteredItems.Add(item);
        }

        HashSet<int> filteredItemIds =
            filteredItems
                .Select(item =>
                    item.Id)
                .ToHashSet();

        List<AccessibilityRemediationHistory> remediationHistory;

        if (filteredItemIds.Count == 0)
        {
            remediationHistory =
                new List<AccessibilityRemediationHistory>();
        }
        else
        {
            remediationHistory =
                await _dbContext.AccessibilityRemediationHistories
                    .AsNoTracking()
                    .Where(history =>
                        filteredItemIds.Contains(
                            history.AccessibilityRemediationItemId))
                    .ToListAsync(cancellationToken);
        }

        List<AccessibilityRemediationTrendPointViewModel>
            remediationTrends =
                remediationHistory
                    .Where(history =>
                        string.Equals(
                            history.EventType,
                            "Verified",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            history.EventType,
                            "Reopened",
                            StringComparison.OrdinalIgnoreCase))
                    .GroupBy(history =>
                        history.ChangedAt.Date)
                    .Select(group =>
                        new AccessibilityRemediationTrendPointViewModel
                        {
                            Date =
                                group.Key,

                            Verified =
                                group.Count(history =>
                                    string.Equals(
                                        history.EventType,
                                        "Verified",
                                        StringComparison.OrdinalIgnoreCase)),

                            Reopened =
                                group.Count(history =>
                                    string.Equals(
                                        history.EventType,
                                        "Reopened",
                                        StringComparison.OrdinalIgnoreCase))
                        })
                    .OrderBy(point =>
                        point.Date)
                    .ToList();

        List<AccessibilityApplicationRemediationProgressViewModel>
            applicationProgress =
                filteredItems
                    .GroupBy(
                        item =>
                        {
                            AccessibilityRemediationFindingOccurrence
                                originalOccurrence =
                                    item.FindingOccurrences
                                        .OrderBy(occurrence =>
                                            occurrence.LinkedAt)
                                        .ThenBy(occurrence =>
                                            occurrence.Id)
                                        .First();

                            return originalOccurrence
                                .AuthenticatedAuditFinding
                                .AuthenticatedAuditStep
                                .AuthenticatedAuditRun
                                .ApplicationName
                                .Trim();
                        },
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                        new AccessibilityApplicationRemediationProgressViewModel
                        {
                            ApplicationName =
                                group.Key,

                            TotalTracked =
                                group.Count(),

                            Open =
                                group.Count(item =>
                                    item.Status ==
                                    AccessibilityRemediationStatus.Open),

                            InProgress =
                                group.Count(item =>
                                    item.Status ==
                                    AccessibilityRemediationStatus.InProgress),

                            AwaitingVerification =
                                group.Count(item =>
                                    item.Status ==
                                    AccessibilityRemediationStatus.Fixed),

                            Verified =
                                group.Count(item =>
                                    item.Status ==
                                    AccessibilityRemediationStatus.Verified),

                            WontFix =
                                group.Count(item =>
                                    item.Status ==
                                    AccessibilityRemediationStatus.WontFix)
                        })
                    .OrderBy(application =>
                        application.VerifiedPercent)
                    .ThenByDescending(application =>
                        application.Remaining)
                    .ThenBy(application =>
                        application.ApplicationName)
                    .ToList();

        /*
         * Formal workflow progress uses the same filtered remediation
         * item set as the rest of this section.
         *
         * We intentionally do not filter formal retests by RetestedAt.
         * The dashboard's date filter identifies the original audit
         * findings in scope, while the remediation status represents
         * their current lifecycle state.
         */
        AccessibilityFormalWorkflowProgressViewModel
            formalWorkflowProgress =
                await LoadFormalWorkflowProgressAsync(
                    filteredItemIds,
                    remediationHistory,
                    cancellationToken);

        return new AccessibilityRemediationProgressViewModel
        {
            TotalTracked =
                filteredItems.Count,

            Open =
                filteredItems.Count(item =>
                    item.Status ==
                    AccessibilityRemediationStatus.Open),

            InProgress =
                filteredItems.Count(item =>
                    item.Status ==
                    AccessibilityRemediationStatus.InProgress),

            AwaitingVerification =
                filteredItems.Count(item =>
                    item.Status ==
                    AccessibilityRemediationStatus.Fixed),

            Verified =
                filteredItems.Count(item =>
                    item.Status ==
                    AccessibilityRemediationStatus.Verified),

            WontFix =
                filteredItems.Count(item =>
                    item.Status ==
                    AccessibilityRemediationStatus.WontFix),

            FormalWorkflow =
                formalWorkflowProgress,

            Applications =
                applicationProgress,

            Trends =
                remediationTrends
        };
    }

    /// <summary>
    /// Builds management-level formal workflow retest progress for the
    /// remediation items currently in scope.
    ///
    /// Verification credit is only given when the formal workflow retest
    /// remains the latest retest evidence for that remediation item.
    /// </summary>
    private async Task<AccessibilityFormalWorkflowProgressViewModel>
        LoadFormalWorkflowProgressAsync(
            IReadOnlyCollection<int> remediationItemIds,
            IReadOnlyCollection<AccessibilityRemediationHistory>
                remediationHistory,
            CancellationToken cancellationToken)
    {
        if (remediationItemIds.Count == 0)
        {
            return new AccessibilityFormalWorkflowProgressViewModel();
        }

        List<int> itemIds =
            remediationItemIds
                .Distinct()
                .ToList();

        List<AccessibilityRemediationRetest> formalRetests =
            await _dbContext.AccessibilityRemediationRetests
                .AsNoTracking()
                .Where(retest =>
                    itemIds.Contains(
                        retest.AccessibilityRemediationItemId) &&
                    retest.RetestType == "FullWorkflow" &&
                    retest.AuthenticatedAuditRunId.HasValue)
                .Include(retest =>
                    retest.AuthenticatedAuditRun)
                .Include(retest =>
                    retest.RemediationItem)
                .ToListAsync(cancellationToken);

        if (formalRetests.Count == 0)
        {
            return new AccessibilityFormalWorkflowProgressViewModel();
        }

        /*
         * A formal workflow retest can only support current verification
         * if it is still the newest retest evidence for that item.
         *
         * Current-state retests are deliberately included in this lookup.
         */
        List<LatestRetestSnapshot> retestEvidence =
            await _dbContext.AccessibilityRemediationRetests
                .AsNoTracking()
                .Where(retest =>
                    itemIds.Contains(
                        retest.AccessibilityRemediationItemId))
                .Select(retest =>
                    new LatestRetestSnapshot
                    {
                        Id =
                            retest.Id,

                        AccessibilityRemediationItemId =
                            retest.AccessibilityRemediationItemId,

                        RetestedAt =
                            retest.RetestedAt
                    })
                .ToListAsync(cancellationToken);

        Dictionary<int, int> latestRetestIds =
            retestEvidence
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

        List<(
            AccessibilityApplicationFormalWorkflowProgressViewModel Progress,
            DateTime RunStartedAt)> formalRunProgress =
                new();

        IEnumerable<IGrouping<int, AccessibilityRemediationRetest>>
            runGroups =
                formalRetests
                    .GroupBy(retest =>
                        retest.AuthenticatedAuditRunId!.Value);

        foreach (IGrouping<int, AccessibilityRemediationRetest>
            runGroup in runGroups)
        {
            AccessibilityRemediationRetest first =
                runGroup.First();

            AuthenticatedAuditRun? auditRun =
                first.AuthenticatedAuditRun;

            if (auditRun is null ||
                string.IsNullOrWhiteSpace(
                    auditRun.ApplicationName))
            {
                continue;
            }

            int totalTracked =
                runGroup.Count();

            int stillDetected =
                runGroup.Count(retest =>
                    retest.Result ==
                    AccessibilityRemediationRetestResult.Detected);

            int notDetected =
                runGroup.Count(retest =>
                    retest.Result ==
                    AccessibilityRemediationRetestResult.NotDetected);

            int inconclusive =
                runGroup.Count(retest =>
                    retest.Result ==
                    AccessibilityRemediationRetestResult.Inconclusive);

            int failed =
                runGroup.Count(retest =>
                    retest.Result ==
                    AccessibilityRemediationRetestResult.Failed);

            int verifiedFromThisRetest =
                runGroup.Count(retest =>
                    retest.Result ==
                        AccessibilityRemediationRetestResult.NotDetected &&
                    retest.RemediationItem.Status ==
                        AccessibilityRemediationStatus.Verified &&
                    latestRetestIds.TryGetValue(
                        retest.AccessibilityRemediationItemId,
                        out int latestRetestId) &&
                    latestRetestId ==
                        retest.Id);

            int reopened =
                runGroup.Count(retest =>
                    remediationHistory.Any(history =>
                        history.AccessibilityRemediationItemId ==
                            retest.AccessibilityRemediationItemId &&
                        string.Equals(
                            history.EventType,
                            "Reopened",
                            StringComparison.OrdinalIgnoreCase) &&
                        history.ChangedAt ==
                            retest.RetestedAt));

            string verificationState =
                GetFormalVerificationState(
                    notDetected,
                    verifiedFromThisRetest);

            DateTime recordedAt =
                runGroup.Max(retest =>
                    retest.RetestedAt);

            AccessibilityApplicationFormalWorkflowProgressViewModel
                progress =
                    new()
                    {
                        ApplicationName =
                            auditRun.ApplicationName.Trim(),

                        RetestAuditRunId =
                            runGroup.Key,

                        RetestedAt =
                            recordedAt,

                        TotalTracked =
                            totalTracked,

                        StillDetected =
                            stillDetected,

                        NotDetected =
                            notDetected,

                        VerifiedFromThisRetest =
                            verifiedFromThisRetest,

                        Inconclusive =
                            inconclusive,

                        Failed =
                            failed,

                        Reopened =
                            reopened,

                        VerificationState =
                            verificationState
                    };

            formalRunProgress.Add(
                (
                    progress,
                    auditRun.StartedAt
                ));
        }

        if (formalRunProgress.Count == 0)
        {
            return new AccessibilityFormalWorkflowProgressViewModel();
        }

        /*
         * Management primarily needs the latest formal workflow status for
         * each application, while TotalFormalRetestRuns preserves the full
         * historical count.
         *
         * "Latest" is based on the authenticated audit run date, not the date
         * somebody happened to click Apply Formal Workflow Retest.
         */
        List<(
            AccessibilityApplicationFormalWorkflowProgressViewModel Progress,
            DateTime RunStartedAt)> latestPerApplication =
                formalRunProgress
                    .GroupBy(
                        item =>
                            item.Progress.ApplicationName.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                        group
                            .OrderByDescending(item =>
                                item.RunStartedAt)
                            .ThenByDescending(item =>
                                item.Progress.RetestAuditRunId)
                            .First())
                    .ToList();

        List<AccessibilityApplicationFormalWorkflowProgressViewModel>
            applicationProgress =
                latestPerApplication
                    .Select(item =>
                        item.Progress)
                    .OrderBy(progress =>
                        GetFormalVerificationStateRank(
                            progress.VerificationState))
                    .ThenByDescending(progress =>
                        progress.StillDetected)
                    .ThenByDescending(progress =>
                        progress.Inconclusive +
                        progress.Failed)
                    .ThenBy(progress =>
                        progress.ApplicationName)
                    .ToList();

        return new AccessibilityFormalWorkflowProgressViewModel
        {
            TotalFormalRetestRuns =
                formalRunProgress.Count,

            ApplicationsFormallyRetested =
                applicationProgress.Count,

            LatestApplied =
                applicationProgress.Count(progress =>
                    progress.VerificationState ==
                    "Applied"),

            LatestPartiallyVerified =
                applicationProgress.Count(progress =>
                    progress.VerificationState ==
                    "Partially Verified"),

            LatestFullyVerified =
                applicationProgress.Count(progress =>
                    progress.VerificationState ==
                    "Fully Verified"),

            LatestFormalRetestAt =
                formalRunProgress
                    .Max(item =>
                        item.Progress.RetestedAt),

            Applications =
                applicationProgress
        };
    }

    private async Task<List<AuthenticatedRunSnapshot>>
        LoadAuthenticatedRunsAsync(
            string? applicationName,
            DateTime? startDate,
            DateTime? endDateExclusive,
            CancellationToken cancellationToken)
    {
        IQueryable<AuthenticatedAuditRun> query =
            _dbContext.AuthenticatedAuditRuns
                .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(applicationName))
        {
            query =
                query.Where(run =>
                    run.ApplicationName ==
                    applicationName);
        }

        if (startDate.HasValue)
        {
            query =
                query.Where(run =>
                    run.StartedAt >=
                    startDate.Value);
        }

        if (endDateExclusive.HasValue)
        {
            query =
                query.Where(run =>
                    run.StartedAt <
                    endDateExclusive.Value);
        }

        return await query
            .Select(run =>
                new AuthenticatedRunSnapshot
                {
                    Id =
                        run.Id,

                    ApplicationName =
                        run.ApplicationName,

                    StartingUrl =
                        run.StartingUrl,

                    Status =
                        run.Status,

                    StartedAt =
                        run.StartedAt
                })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AuthenticatedStepSnapshot>>
        LoadAuthenticatedStepsAsync(
            IReadOnlyCollection<int> runIds,
            CancellationToken cancellationToken)
    {
        if (runIds.Count == 0)
        {
            return new List<AuthenticatedStepSnapshot>();
        }

        return await _dbContext.AuthenticatedAuditSteps
            .AsNoTracking()
            .Where(step =>
                runIds.Contains(
                    step.AuthenticatedAuditRunId))
            .Select(step =>
                new AuthenticatedStepSnapshot
                {
                    Id =
                        step.Id,

                    AuthenticatedAuditRunId =
                        step.AuthenticatedAuditRunId,

                    ScannedAt =
                        step.ScannedAt,

                    ViolationRuleCount =
                        step.ViolationRuleCount,

                    AffectedElementCount =
                        step.AffectedElementCount,

                    NeedsReviewRuleCount =
                        step.NeedsReviewRuleCount,

                    PassedRuleCount =
                        step.PassedRuleCount,

                    ScanSucceeded =
                        step.ScanSucceeded
                })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AuthenticatedFindingSnapshot>>
        LoadAuthenticatedFindingsAsync(
            IReadOnlyCollection<int> stepIds,
            CancellationToken cancellationToken)
    {
        if (stepIds.Count == 0)
        {
            return new List<AuthenticatedFindingSnapshot>();
        }

        return await _dbContext.AuthenticatedAuditFindings
            .AsNoTracking()
            .Where(finding =>
                stepIds.Contains(
                    finding.AuthenticatedAuditStepId))
            .Select(finding =>
                new AuthenticatedFindingSnapshot
                {
                    AuthenticatedAuditStepId =
                        finding.AuthenticatedAuditStepId,

                    FindingType =
                        finding.FindingType,

                    RuleId =
                        finding.RuleId,

                    Impact =
                        finding.Impact,

                    WcagLevel =
                        finding.WcagLevel,

                    Help =
                        finding.Help,

                    Description =
                        finding.Description,

                    HelpUrl =
                        finding.HelpUrl,

                    AffectedElementCount =
                        finding.AffectedElementCount
                })
            .ToListAsync(cancellationToken);
    }

    private static List<AuthenticatedRunSnapshot>
        SelectAuthenticatedRuns(
            IReadOnlyCollection<AuthenticatedRunSnapshot> runs,
            bool latestOnly)
    {
        if (!latestOnly)
        {
            return runs
                .OrderByDescending(run =>
                    run.StartedAt)
                .ThenByDescending(run =>
                    run.Id)
                .ToList();
        }

        return runs
            .GroupBy(
                run =>
                    run.ApplicationName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group
                    .OrderByDescending(run =>
                        run.StartedAt)
                    .ThenByDescending(run =>
                        run.Id)
                    .First())
            .OrderByDescending(run =>
                run.StartedAt)
            .ThenBy(run =>
                run.ApplicationName)
            .ToList();
    }

    private static List<AuthenticatedRunSnapshot>
        SelectLatestAndPreviousAuthenticatedRuns(
            IReadOnlyCollection<AuthenticatedRunSnapshot> runs)
    {
        return runs
            .GroupBy(
                run =>
                    run.ApplicationName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
                group
                    .OrderByDescending(run =>
                        run.StartedAt)
                    .ThenByDescending(run =>
                        run.Id)
                    .Take(2))
            .OrderBy(run =>
                run.ApplicationName)
            .ThenByDescending(run =>
                run.StartedAt)
            .ThenByDescending(run =>
                run.Id)
            .ToList();
    }

    private static List<AuthenticatedFindingSnapshot>
        ApplyFindingFilters(
            IEnumerable<AuthenticatedFindingSnapshot> findings,
            string? severity,
            string? wcagLevel,
            string? findingType)
    {
        IEnumerable<AuthenticatedFindingSnapshot> query =
            findings;

        if (!string.IsNullOrWhiteSpace(severity))
        {
            if (severity == "Unknown")
            {
                query =
                    query.Where(finding =>
                        !IsKnownImpact(
                            finding.Impact));
            }
            else
            {
                query =
                    query.Where(finding =>
                        string.Equals(
                            finding.Impact,
                            severity,
                            StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(wcagLevel))
        {
            if (wcagLevel == "Unmapped")
            {
                query =
                    query.Where(finding =>
                        !IsWcagLevel(
                            finding.WcagLevel,
                            "A") &&
                        !IsWcagLevel(
                            finding.WcagLevel,
                            "AA"));
            }
            else
            {
                query =
                    query.Where(finding =>
                        IsWcagLevel(
                            finding.WcagLevel,
                            wcagLevel));
            }
        }

        if (!string.IsNullOrWhiteSpace(findingType))
        {
            query =
                query.Where(finding =>
                    string.Equals(
                        finding.FindingType,
                        findingType,
                        StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    private static List<AccessibilityApplicationRankingViewModel>
        BuildApplicationRankings(
            IReadOnlyCollection<AuthenticatedRunSnapshot> runs,
            IReadOnlyCollection<AuthenticatedStepSnapshot> steps,
            IReadOnlyCollection<AuthenticatedFindingSnapshot> findings)
    {
        List<AccessibilityApplicationRankingViewModel> rankings =
            new();

        IEnumerable<IGrouping<string, AuthenticatedRunSnapshot>>
            applicationGroups =
                runs.GroupBy(
                    run =>
                        run.ApplicationName.Trim(),
                    StringComparer.OrdinalIgnoreCase);

        foreach (
            IGrouping<string, AuthenticatedRunSnapshot> applicationGroup
            in applicationGroups)
        {
            List<AuthenticatedRunSnapshot> orderedRuns =
                applicationGroup
                    .OrderByDescending(run =>
                        run.StartedAt)
                    .ThenByDescending(run =>
                        run.Id)
                    .ToList();

            AuthenticatedRunSnapshot latestRun =
                orderedRuns[0];

            AuthenticatedRunSnapshot? previousRun =
                orderedRuns
                    .Skip(1)
                    .FirstOrDefault();

            List<AuthenticatedStepSnapshot> latestRunSteps =
                steps
                    .Where(step =>
                        step.AuthenticatedAuditRunId ==
                        latestRun.Id)
                    .ToList();

            HashSet<int> latestStepIds =
                latestRunSteps
                    .Select(step =>
                        step.Id)
                    .ToHashSet();

            List<AuthenticatedFindingSnapshot> latestRunFindings =
                findings
                    .Where(finding =>
                        latestStepIds.Contains(
                            finding.AuthenticatedAuditStepId))
                    .ToList();

            List<AuthenticatedFindingSnapshot> latestViolationFindings =
                latestRunFindings
                    .Where(IsViolation)
                    .ToList();

            int? previousFindingCount =
                null;

            int? previousStateCount =
                null;

            if (previousRun is not null)
            {
                List<AuthenticatedStepSnapshot> previousRunSteps =
                    steps
                        .Where(step =>
                            step.AuthenticatedAuditRunId ==
                            previousRun.Id)
                        .ToList();

                HashSet<int> previousStepIds =
                    previousRunSteps
                        .Select(step =>
                            step.Id)
                        .ToHashSet();

                previousFindingCount =
                    findings.Count(finding =>
                        previousStepIds.Contains(
                            finding.AuthenticatedAuditStepId));

                previousStateCount =
                    previousRunSteps.Count;
            }

            rankings.Add(
                new AccessibilityApplicationRankingViewModel
                {
                    ApplicationName =
                        latestRun.ApplicationName,

                    LatestRunId =
                        latestRun.Id,

                    StartingUrl =
                        latestRun.StartingUrl,

                    Status =
                        latestRun.Status,

                    LatestAuditDate =
                        latestRun.StartedAt,

                    StateCount =
                        latestRunSteps.Count,

                    SuccessfulStateCount =
                        latestRunSteps.Count(step =>
                            step.ScanSucceeded),

                    CriticalFindingCount =
                        latestViolationFindings.Count(finding =>
                            IsImpact(
                                finding.Impact,
                                "Critical")),

                    SeriousFindingCount =
                        latestViolationFindings.Count(finding =>
                            IsImpact(
                                finding.Impact,
                                "Serious")),

                    FixFirstFindingCount =
                        latestRunFindings.Count(
                            IsFixFirst),

                    NeedsReviewFindingCount =
                        latestRunFindings.Count(finding =>
                            string.Equals(
                                finding.FindingType,
                                "NeedsReview",
                                StringComparison.OrdinalIgnoreCase)),

                    AffectedElementCount =
                        latestRunFindings.Sum(finding =>
                            finding.AffectedElementCount),

                    TotalFindingCount =
                        latestRunFindings.Count,

                    PreviousRunId =
                        previousRun?.Id,

                    PreviousFindingCount =
                        previousFindingCount,

                    PreviousStateCount =
                        previousStateCount
                });
        }

        return rankings
            .OrderByDescending(application =>
                application.FixFirstFindingCount)
            .ThenByDescending(application =>
                application.CriticalFindingCount)
            .ThenByDescending(application =>
                application.SeriousFindingCount)
            .ThenByDescending(application =>
                application.TotalFindingCount)
            .ThenBy(application =>
                application.ApplicationName)
            .Take(10)
            .ToList();
    }

    private static List<AccessibilityTopFindingViewModel>
        BuildTopFindings(
            IReadOnlyCollection<AuthenticatedRunSnapshot> runs,
            IReadOnlyCollection<AuthenticatedStepSnapshot> steps,
            IReadOnlyCollection<AuthenticatedFindingSnapshot> findings)
    {
        Dictionary<int, AuthenticatedRunSnapshot> runsById =
            runs.ToDictionary(run =>
                run.Id);

        Dictionary<int, AuthenticatedStepSnapshot> stepsById =
            steps.ToDictionary(step =>
                step.Id);

        IEnumerable<IGrouping<string, AuthenticatedFindingSnapshot>>
            findingGroups =
                findings
                    .Where(finding =>
                        !string.IsNullOrWhiteSpace(
                            finding.RuleId))
                    .GroupBy(finding =>
                        string.Concat(
                            finding.RuleId
                                .Trim()
                                .ToLowerInvariant(),
                            "|",
                            finding.FindingType
                                .Trim()
                                .ToLowerInvariant()));

        List<(
            AccessibilityTopFindingViewModel Finding,
            int Priority)> rankedFindings =
                new();

        foreach (
            IGrouping<string, AuthenticatedFindingSnapshot> findingGroup
            in findingGroups)
        {
            List<(
                AuthenticatedFindingSnapshot Finding,
                AuthenticatedRunSnapshot Run)> occurrences =
                    new();

            foreach (
                AuthenticatedFindingSnapshot finding
                in findingGroup)
            {
                if (!stepsById.TryGetValue(
                    finding.AuthenticatedAuditStepId,
                    out AuthenticatedStepSnapshot? step))
                {
                    continue;
                }

                if (!runsById.TryGetValue(
                    step.AuthenticatedAuditRunId,
                    out AuthenticatedRunSnapshot? run))
                {
                    continue;
                }

                occurrences.Add(
                    (
                        finding,
                        run
                    ));
            }

            if (occurrences.Count == 0)
            {
                continue;
            }

            (
                AuthenticatedFindingSnapshot Finding,
                AuthenticatedRunSnapshot Run) displayOccurrence =
                    occurrences
                        .OrderBy(occurrence =>
                            GetFindingPriorityRank(
                                occurrence.Finding))
                        .ThenByDescending(occurrence =>
                            occurrence.Run.StartedAt)
                        .ThenByDescending(occurrence =>
                            occurrence.Run.Id)
                        .First();

            (
                AuthenticatedFindingSnapshot Finding,
                AuthenticatedRunSnapshot Run) latestOccurrence =
                    occurrences
                        .OrderByDescending(occurrence =>
                            occurrence.Run.StartedAt)
                        .ThenByDescending(occurrence =>
                            occurrence.Run.Id)
                        .First();

            AccessibilityTopFindingViewModel topFinding =
                new()
                {
                    RuleId =
                        displayOccurrence.Finding.RuleId,

                    FindingType =
                        displayOccurrence.Finding.FindingType,

                    Impact =
                        displayOccurrence.Finding.Impact,

                    WcagLevel =
                        displayOccurrence.Finding.WcagLevel,

                    Help =
                        displayOccurrence.Finding.Help,

                    Description =
                        displayOccurrence.Finding.Description,

                    HelpUrl =
                        displayOccurrence.Finding.HelpUrl,

                    ApplicationCount =
                        occurrences
                            .Select(occurrence =>
                                occurrence.Run
                                    .ApplicationName
                                    .Trim())
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .Count(),

                    StateCount =
                        occurrences
                            .Select(occurrence =>
                                occurrence.Finding
                                    .AuthenticatedAuditStepId)
                            .Distinct()
                            .Count(),

                    AffectedElementCount =
                        occurrences.Sum(occurrence =>
                            occurrence.Finding
                                .AffectedElementCount),

                    LatestRunId =
                        latestOccurrence.Run.Id,

                    LatestApplicationName =
                        latestOccurrence.Run.ApplicationName
                };

            rankedFindings.Add(
                (
                    topFinding,
                    GetFindingPriorityRank(
                        displayOccurrence.Finding)
                ));
        }

        return rankedFindings
            .OrderBy(item =>
                item.Priority)
            .ThenByDescending(item =>
                item.Finding.ApplicationCount)
            .ThenByDescending(item =>
                item.Finding.StateCount)
            .ThenByDescending(item =>
                item.Finding.AffectedElementCount)
            .ThenBy(item =>
                item.Finding.RuleId)
            .Take(15)
            .Select(item =>
                item.Finding)
            .ToList();
    }

    private static int GetFindingPriorityRank(
        AuthenticatedFindingSnapshot finding)
    {
        int severityRank =
            finding.Impact?
                .Trim()
                .ToLowerInvariant() switch
            {
                "critical" => 0,
                "serious" => 3,
                "moderate" => 6,
                "minor" => 9,
                _ => 12
            };

        int wcagRank =
            finding.WcagLevel?
                .Trim()
                .ToUpperInvariant() switch
            {
                "A" => 1,
                "AA" => 2,
                _ => 3
            };

        return severityRank +
               wcagRank;
    }

    private static List<AccessibilityTrendPointViewModel>
        BuildTrendPoints(
            IReadOnlyCollection<AuthenticatedStepSnapshot> steps,
            IReadOnlyCollection<AuthenticatedFindingSnapshot> findings)
    {
        Dictionary<int, DateTime> stepDatesById =
            steps.ToDictionary(
                step =>
                    step.Id,
                step =>
                    step.ScannedAt.Date);

        List<DateTime> reportingDates =
            steps
                .Select(step =>
                    step.ScannedAt.Date)
                .Distinct()
                .OrderBy(date =>
                    date)
                .ToList();

        List<AccessibilityTrendPointViewModel> trends =
            new();

        foreach (DateTime reportingDate
            in reportingDates)
        {
            List<AuthenticatedFindingSnapshot> findingsForDate =
                findings
                    .Where(finding =>
                        stepDatesById.TryGetValue(
                            finding.AuthenticatedAuditStepId,
                            out DateTime findingDate) &&
                        findingDate ==
                        reportingDate)
                    .ToList();

            trends.Add(
                new AccessibilityTrendPointViewModel
                {
                    Date =
                        reportingDate,

                    AuthenticatedStatesScanned =
                        steps.Count(step =>
                            step.ScannedAt.Date ==
                            reportingDate),

                    AuthenticatedFindings =
                        findingsForDate.Count(
                            IsViolation),

                    FixFirstFindings =
                        findingsForDate.Count(
                            IsFixFirst)
                });
        }

        return trends;
    }

    private static AccessibilityOverviewSummaryViewModel
        BuildSummary(
            IReadOnlyCollection<AuthenticatedRunSnapshot> runs,
            IReadOnlyCollection<AuthenticatedStepSnapshot> steps,
            IReadOnlyCollection<AuthenticatedFindingSnapshot> findings)
    {
        List<AuthenticatedFindingSnapshot> violationFindings =
            findings
                .Where(IsViolation)
                .ToList();

        return new AccessibilityOverviewSummaryViewModel
        {
            ApplicationsAudited =
                runs
                    .Select(run =>
                        run.ApplicationName.Trim())
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .Count(),

            AuthenticatedStatesScanned =
                steps.Count,

            TotalAutomatedFindings =
                violationFindings.Count,

            TotalAffectedElements =
                findings.Sum(finding =>
                    finding.AffectedElementCount),

            FixFirstFindings =
                findings.Count(
                    IsFixFirst),

            /*
             * This overview intentionally reports authenticated axe-core
             * workflow results. Public WAVE scan counts are not mixed into
             * this management metric.
             */
            PublicPagesWithFindings =
                0,

            AuthenticatedStatesWithFindings =
                findings
                    .Select(finding =>
                        finding.AuthenticatedAuditStepId)
                    .Distinct()
                    .Count()
        };
    }

    private static AccessibilityHealthViewModel
        BuildHealth(
            IReadOnlyCollection<AuthenticatedStepSnapshot> steps,
            IReadOnlyCollection<AuthenticatedFindingSnapshot> findings)
    {
        int passedRuleResults =
            steps.Sum(step =>
                step.PassedRuleCount);

        int violationRuleResults =
            findings.Count(
                IsViolation);

        int needsReviewRuleResults =
            findings.Count(finding =>
                string.Equals(
                    finding.FindingType,
                    "NeedsReview",
                    StringComparison.OrdinalIgnoreCase));

        int totalRuleResults =
            passedRuleResults +
            violationRuleResults +
            needsReviewRuleResults;

        double automatedCheckPassRate =
            totalRuleResults == 0
                ? 0
                : passedRuleResults *
                  100.0 /
                  totalRuleResults;

        return new AccessibilityHealthViewModel
        {
            PassedRuleResults =
                passedRuleResults,

            ViolationRuleResults =
                violationRuleResults,

            NeedsReviewRuleResults =
                needsReviewRuleResults,

            TotalRuleResults =
                totalRuleResults,

            AutomatedCheckPassRate =
                automatedCheckPassRate,

            WcagLevelAFindingCount =
                findings.Count(finding =>
                    IsWcagLevel(
                        finding.WcagLevel,
                        "A")),

            WcagLevelAAFindingCount =
                findings.Count(finding =>
                    IsWcagLevel(
                        finding.WcagLevel,
                        "AA")),

            BestPracticeOrUnmappedFindingCount =
                findings.Count(finding =>
                    !IsWcagLevel(
                        finding.WcagLevel,
                        "A") &&
                    !IsWcagLevel(
                        finding.WcagLevel,
                        "AA"))
        };
    }

    private static AccessibilityIssueBreakdownViewModel
        BuildIssueBreakdown(
            IReadOnlyCollection<AuthenticatedFindingSnapshot> findings)
    {
        List<AuthenticatedFindingSnapshot> violations =
            findings
                .Where(IsViolation)
                .ToList();

        return new AccessibilityIssueBreakdownViewModel
        {
            Critical =
                violations.Count(finding =>
                    IsImpact(
                        finding.Impact,
                        "Critical")),

            Serious =
                violations.Count(finding =>
                    IsImpact(
                        finding.Impact,
                        "Serious")),

            Moderate =
                violations.Count(finding =>
                    IsImpact(
                        finding.Impact,
                        "Moderate")),

            Minor =
                violations.Count(finding =>
                    IsImpact(
                        finding.Impact,
                        "Minor")),

            UnknownSeverity =
                violations.Count(finding =>
                    !IsKnownImpact(
                        finding.Impact)),

            NeedsManualReview =
                findings.Count(finding =>
                    string.Equals(
                        finding.FindingType,
                        "NeedsReview",
                        StringComparison.OrdinalIgnoreCase)),

            WcagLevelA =
                findings.Count(finding =>
                    IsWcagLevel(
                        finding.WcagLevel,
                        "A")),

            WcagLevelAA =
                findings.Count(finding =>
                    IsWcagLevel(
                        finding.WcagLevel,
                        "AA")),

            BestPracticeOrUnmapped =
                findings.Count(finding =>
                    !IsWcagLevel(
                        finding.WcagLevel,
                        "A") &&
                    !IsWcagLevel(
                        finding.WcagLevel,
                        "AA"))
        };
    }

    private static string GetFormalVerificationState(
        int notDetected,
        int verifiedFromThisRetest)
    {
        if (notDetected == 0 ||
            verifiedFromThisRetest == 0)
        {
            return "Applied";
        }

        if (verifiedFromThisRetest <
            notDetected)
        {
            return "Partially Verified";
        }

        return "Fully Verified";
    }

    private static int GetFormalVerificationStateRank(
        string verificationState)
    {
        return verificationState switch
        {
            "Applied" =>
                0,

            "Partially Verified" =>
                1,

            "Fully Verified" =>
                2,

            _ =>
                3
        };
    }

    private static bool IsViolation(
        AuthenticatedFindingSnapshot finding)
    {
        return string.Equals(
            finding.FindingType,
            "Violation",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFixFirst(
        AuthenticatedFindingSnapshot finding)
    {
        if (!IsViolation(finding))
        {
            return false;
        }

        bool isCriticalOrSerious =
            IsImpact(
                finding.Impact,
                "Critical") ||
            IsImpact(
                finding.Impact,
                "Serious");

        bool isLevelAOrAA =
            IsWcagLevel(
                finding.WcagLevel,
                "A") ||
            IsWcagLevel(
                finding.WcagLevel,
                "AA");

        return isCriticalOrSerious &&
               isLevelAOrAA;
    }

    private static bool IsKnownImpact(
        string? impact)
    {
        return
            IsImpact(
                impact,
                "Critical") ||
            IsImpact(
                impact,
                "Serious") ||
            IsImpact(
                impact,
                "Moderate") ||
            IsImpact(
                impact,
                "Minor");
    }

    private static bool IsImpact(
        string? actualImpact,
        string expectedImpact)
    {
        return string.Equals(
            actualImpact?.Trim(),
            expectedImpact,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWcagLevel(
        string? actualLevel,
        string expectedLevel)
    {
        return string.Equals(
            actualLevel?.Trim(),
            expectedLevel,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeSeverity(
        string? severity)
    {
        string? normalizedValue =
            NormalizeOptionalValue(severity)?
                .ToLowerInvariant();

        return normalizedValue switch
        {
            "critical" =>
                "Critical",

            "serious" =>
                "Serious",

            "moderate" =>
                "Moderate",

            "minor" =>
                "Minor",

            "unknown" =>
                "Unknown",

            _ =>
                null
        };
    }

    private static string? NormalizeWcagLevel(
        string? wcagLevel)
    {
        string? normalizedValue =
            NormalizeOptionalValue(wcagLevel)?
                .ToUpperInvariant();

        return normalizedValue switch
        {
            "A" =>
                "A",

            "AA" =>
                "AA",

            "UNMAPPED" =>
                "Unmapped",

            _ =>
                null
        };
    }

    private static string? NormalizeFindingType(
        string? findingType)
    {
        string? normalizedValue =
            NormalizeOptionalValue(findingType)?
                .ToLowerInvariant();

        return normalizedValue switch
        {
            "violation" =>
                "Violation",

            "needsreview" =>
                "NeedsReview",

            "needs review" =>
                "NeedsReview",

            _ =>
                null
        };
    }

    private static string? NormalizeOptionalValue(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed class LatestRetestSnapshot
    {
        public int Id { get; init; }

        public int AccessibilityRemediationItemId { get; init; }

        public DateTime RetestedAt { get; init; }
    }

    private sealed class AuthenticatedRunSnapshot
    {
        public int Id { get; init; }

        public string ApplicationName { get; init; }
            = string.Empty;

        public string StartingUrl { get; init; }
            = string.Empty;

        public string Status { get; init; }
            = string.Empty;

        public DateTime StartedAt { get; init; }
    }

    private sealed class AuthenticatedStepSnapshot
    {
        public int Id { get; init; }

        public int AuthenticatedAuditRunId { get; init; }

        public DateTime ScannedAt { get; init; }

        public int ViolationRuleCount { get; init; }

        public int AffectedElementCount { get; init; }

        public int NeedsReviewRuleCount { get; init; }

        public int PassedRuleCount { get; init; }

        public bool ScanSucceeded { get; init; }
    }

    private sealed class AuthenticatedFindingSnapshot
    {
        public int AuthenticatedAuditStepId { get; init; }

        public string FindingType { get; init; }
            = string.Empty;

        public string RuleId { get; init; }
            = string.Empty;

        public string? Impact { get; init; }

        public string? WcagLevel { get; init; }

        public string? Help { get; init; }

        public string? Description { get; init; }

        public string? HelpUrl { get; init; }

        public int AffectedElementCount { get; init; }
    }
}
