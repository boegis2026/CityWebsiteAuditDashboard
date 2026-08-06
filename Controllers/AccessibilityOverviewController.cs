using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using CityWebsiteAuditDashboard.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace CityWebsiteAuditDashboard.Controllers;

/// <summary>
/// Builds the read-only, management-facing accessibility reporting dashboard
/// from saved public WAVE scans and authenticated axe-core audit results.
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
        string auditSource = "All",
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? severity = null,
        string? wcagLevel = null,
        string? findingType = null,
        bool latestOnly = true,
        CancellationToken cancellationToken = default)
    {
        string normalizedAuditSource =
            NormalizeAuditSource(auditSource);

        string? normalizedApplicationName =
            NormalizeOptionalValue(applicationName);

        /*
         * Public WebsiteScan records do not currently have a structured
         * application name. An application filter therefore applies only to
         * authenticated audits.
         */
        if (normalizedAuditSource == "Public")
        {
            normalizedApplicationName = null;
        }

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
                .Select(run => run.ApplicationName)
                .Distinct()
                .OrderBy(applicationNameOption =>
                    applicationNameOption)
                .ToListAsync(cancellationToken);

        bool includeAuthenticated =
            normalizedAuditSource != "Public";

        /*
         * When one authenticated application is selected, public scan data is
         * excluded because the current schema cannot truthfully associate a
         * public WebsiteScan URL with that authenticated ApplicationName.
         */
        bool includePublic =
            normalizedAuditSource != "Authenticated" &&
            normalizedApplicationName is null;

        List<AuthenticatedRunSnapshot> authenticatedRuns =
            await LoadAuthenticatedRunsAsync(
                includeAuthenticated,
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
                .Select(run => run.Id)
                .ToList();

        List<AuthenticatedStepSnapshot> authenticatedSteps =
            await LoadAuthenticatedStepsAsync(
                selectedRunIds,
                cancellationToken);

        List<AuthenticatedStepSnapshot> successfulAuthenticatedSteps =
            authenticatedSteps
                .Where(step => step.ScanSucceeded)
                .ToList();

        List<int> successfulStepIds =
            successfulAuthenticatedSteps
                .Select(step => step.Id)
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

        List<PublicScanSnapshot> publicScans =
            await LoadPublicScansAsync(
                includePublic,
                normalizedStartDate,
                endDateExclusive,
                cancellationToken);

        List<PublicScanSnapshot> selectedPublicScans =
            SelectPublicScans(
                publicScans,
                latestOnly);

        AccessibilityOverviewSummaryViewModel summary =
            BuildSummary(
                selectedAuthenticatedRuns,
                successfulAuthenticatedSteps,
                filteredFindings,
                selectedPublicScans,
                normalizedSeverity,
                normalizedWcagLevel,
                normalizedFindingType);

        AccessibilityHealthViewModel health =
            BuildHealth(
                successfulAuthenticatedSteps,
                filteredFindings,
                selectedPublicScans);

        AccessibilityIssueBreakdownViewModel issueBreakdown =
            BuildIssueBreakdown(filteredFindings);

        List<AuthenticatedRunSnapshot>
            comparisonAuthenticatedRuns =
                SelectLatestAndPreviousAuthenticatedRuns(
                authenticatedRuns);

        List<int> comparisonRunIds =
            comparisonAuthenticatedRuns
                .Select(run => run.Id)
                .ToList();

        List<AuthenticatedStepSnapshot>
            comparisonAuthenticatedSteps =
                await LoadAuthenticatedStepsAsync(
                    comparisonRunIds,
                    cancellationToken);

        List<int> successfulComparisonStepIds =
            comparisonAuthenticatedSteps
                .Where(step => step.ScanSucceeded)
                .Select(step => step.Id)
                .ToList();

        List<AuthenticatedFindingSnapshot>
            comparisonAuthenticatedFindings =
                await LoadAuthenticatedFindingsAsync(
                    successfulComparisonStepIds,
                    cancellationToken);

        List<AuthenticatedFindingSnapshot>
            filteredComparisonFindings =
                ApplyFindingFilters(
                    comparisonAuthenticatedFindings,
                    normalizedSeverity,
                    normalizedWcagLevel,
                    normalizedFindingType);

        List<PublicScanSnapshot>
            latestPublicScansForRanking =
                SelectPublicScans(
                    publicScans,
                    latestOnly: true);

        List<AccessibilityApplicationRankingViewModel>
            applicationRankings =
                BuildApplicationRankings(
                    comparisonAuthenticatedRuns,
                    comparisonAuthenticatedSteps,
                    filteredComparisonFindings);

        List<AccessibilityPublicPageRankingViewModel>
            publicPageRankings =
                BuildPublicPageRankings(
                    latestPublicScansForRanking);

        List<AccessibilityTopFindingViewModel>
            topFindings =
                BuildTopFindings(
                    selectedAuthenticatedRuns,
                    successfulAuthenticatedSteps,
                    filteredFindings);

        List<AccessibilityTrendPointViewModel>
            trendPoints =
                BuildTrendPoints(
                    successfulAuthenticatedSteps,
                    filteredFindings,
                    selectedPublicScans);

        AccessibilityOverviewViewModel model =
            new()
            {
                Filters =
                    new AccessibilityOverviewFilterViewModel
                    {
                        ApplicationName =
                            normalizedApplicationName,

                        AuditSource =
                            normalizedAuditSource,

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

                IssueBreakdown =
                    issueBreakdown,

                Applications =
                    applicationRankings,

                PublicPages =
                    publicPageRankings,

                TopFindings =
                    topFindings,

                Trends =
                    trendPoints
            };

        return View(model);
    }

    private async Task<List<AuthenticatedRunSnapshot>>
        LoadAuthenticatedRunsAsync(
            bool includeAuthenticated,
            string? applicationName,
            DateTime? startDate,
            DateTime? endDateExclusive,
            CancellationToken cancellationToken)
    {
        if (!includeAuthenticated)
        {
            return new List<AuthenticatedRunSnapshot>();
        }

        IQueryable<AuthenticatedAuditRun> query =
            _dbContext.AuthenticatedAuditRuns
                .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(applicationName))
        {
            query = query.Where(run =>
                run.ApplicationName == applicationName);
        }

        if (startDate.HasValue)
        {
            query = query.Where(run =>
                run.StartedAt >= startDate.Value);
        }

        if (endDateExclusive.HasValue)
        {
            query = query.Where(run =>
                run.StartedAt < endDateExclusive.Value);
        }

        return await query
            .Select(run =>
                new AuthenticatedRunSnapshot
                {
                    Id = run.Id,

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
                    Id = step.Id,

                    AuthenticatedAuditRunId =
                        step.AuthenticatedAuditRunId,

                    ScannedAt = step.ScannedAt,

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

    private async Task<List<PublicScanSnapshot>>
        LoadPublicScansAsync(
            bool includePublic,
            DateTime? startDate,
            DateTime? endDateExclusive,
            CancellationToken cancellationToken)
    {
        if (!includePublic)
        {
            return new List<PublicScanSnapshot>();
        }

        /*
         * WaveScanSucceeded receives either true or false whenever a WAVE scan
         * was attempted. Normal website-only scans remain null and are excluded
         * from the accessibility overview.
         */
        IQueryable<WebsiteScan> query =
            _dbContext.WebsiteScans
                .AsNoTracking()
                .Where(scan =>
                    scan.WaveScanSucceeded.HasValue);

        if (startDate.HasValue)
        {
            query = query.Where(scan =>
                (scan.WaveScannedAt ??
                 scan.DateScanned) >=
                startDate.Value);
        }

        if (endDateExclusive.HasValue)
        {
            query = query.Where(scan =>
                (scan.WaveScannedAt ??
                 scan.DateScanned) <
                endDateExclusive.Value);
        }

        return await query
            .Select(scan =>
                new PublicScanSnapshot
                {
                    Id = scan.Id,
                    Url = scan.Url,
                    DateScanned = scan.DateScanned,
                    WaveScannedAt = scan.WaveScannedAt,

                    WaveScanSucceeded =
                        scan.WaveScanSucceeded == true,

                    WaveErrors =
                        scan.WaveErrors ?? 0,

                    WaveContrastErrors =
                        scan.WaveContrastErrors ?? 0,

                    WaveAlerts =
                        scan.WaveAlerts ?? 0,

                    WaveFeatures =
                        scan.WaveFeatures ?? 0,

                    WaveAria =
                        scan.WaveAria ?? 0,

                    WaveErrorMessage =
                        scan.WaveErrorMessage
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
                .OrderByDescending(run => run.StartedAt)
                .ThenByDescending(run => run.Id)
                .ToList();
        }

        return runs
            .GroupBy(
                run => run.ApplicationName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group
                    .OrderByDescending(run => run.StartedAt)
                    .ThenByDescending(run => run.Id)
                    .First())
            .OrderByDescending(run => run.StartedAt)
            .ThenBy(run => run.ApplicationName)
            .ToList();
    }

    private static List<AuthenticatedRunSnapshot>
    SelectLatestAndPreviousAuthenticatedRuns(
        IReadOnlyCollection<AuthenticatedRunSnapshot> runs)
    {
        return runs
            .GroupBy(
                run => run.ApplicationName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
                group
                    .OrderByDescending(run => run.StartedAt)
                    .ThenByDescending(run => run.Id)
                    .Take(2))
            .OrderBy(run => run.ApplicationName)
            .ThenByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .ToList();
    }

    private static List<PublicScanSnapshot>
        SelectPublicScans(
            IReadOnlyCollection<PublicScanSnapshot> scans,
            bool latestOnly)
    {
        if (!latestOnly)
        {
            return scans
                .OrderByDescending(scan =>
                    scan.EffectiveScanDate)
                .ThenByDescending(scan => scan.Id)
                .ToList();
        }

        /*
         * Public URLs are grouped by their exact trimmed saved value. The
         * database does not currently contain a canonical application or URL
         * identity, so the dashboard does not make unsupported assumptions
         * about whether differently written URLs represent the same page.
         */
        return scans
            .GroupBy(
                scan => scan.Url.Trim(),
                StringComparer.Ordinal)
            .Select(group =>
                group
                    .OrderByDescending(scan =>
                        scan.EffectiveScanDate)
                    .ThenByDescending(scan => scan.Id)
                    .First())
            .OrderByDescending(scan =>
                scan.EffectiveScanDate)
            .ThenBy(scan => scan.Url)
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
                query = query.Where(finding =>
                    !IsKnownImpact(finding.Impact));
            }
            else
            {
                query = query.Where(finding =>
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
                query = query.Where(finding =>
                    !IsWcagLevel(
                        finding.WcagLevel,
                        "A") &&
                    !IsWcagLevel(
                        finding.WcagLevel,
                        "AA"));
            }
            else
            {
                query = query.Where(finding =>
                    IsWcagLevel(
                        finding.WcagLevel,
                        wcagLevel));
            }
        }

        if (!string.IsNullOrWhiteSpace(findingType))
        {
            query = query.Where(finding =>
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
                    run => run.ApplicationName.Trim(),
                    StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, AuthenticatedRunSnapshot>
            applicationGroup in applicationGroups)
        {
            List<AuthenticatedRunSnapshot> orderedRuns =
                applicationGroup
                    .OrderByDescending(run => run.StartedAt)
                    .ThenByDescending(run => run.Id)
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
                    .Select(step => step.Id)
                    .ToHashSet();

            List<AuthenticatedFindingSnapshot> latestRunFindings =
                findings
                    .Where(finding =>
                        latestStepIds.Contains(
                            finding.AuthenticatedAuditStepId))
                    .ToList();

            List<AuthenticatedFindingSnapshot>
                latestViolationFindings =
                    latestRunFindings
                        .Where(IsViolation)
                        .ToList();

            int? previousFindingCount = null;
            int? previousStateCount = null;

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
                        .Select(step => step.Id)
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
                        latestRunFindings.Count(IsFixFirst),

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

    private static List<AccessibilityPublicPageRankingViewModel>
        BuildPublicPageRankings(
            IReadOnlyCollection<PublicScanSnapshot> scans)
    {
        return scans
            .Select(scan =>
                new AccessibilityPublicPageRankingViewModel
                {
                    WebsiteScanId =
                        scan.Id,

                    Url =
                        scan.Url,

                    DateScanned =
                        scan.EffectiveScanDate,

                    WaveScanSucceeded =
                        scan.WaveScanSucceeded,

                    WaveErrors =
                        scan.WaveErrors,

                    WaveContrastErrors =
                        scan.WaveContrastErrors,

                    WaveAlerts =
                        scan.WaveAlerts,

                    WaveErrorMessage =
                        scan.WaveErrorMessage
                })
            .OrderByDescending(page =>
                page.WaveErrorAndContrastCount)
            .ThenByDescending(page =>
                page.WaveAlerts)
            .ThenBy(page =>
                page.Url)
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
            runs.ToDictionary(run => run.Id);

        Dictionary<int, AuthenticatedStepSnapshot> stepsById =
            steps.ToDictionary(step => step.Id);

        IEnumerable<IGrouping<string, AuthenticatedFindingSnapshot>>
            findingGroups =
                findings
                    .Where(finding =>
                        !string.IsNullOrWhiteSpace(
                            finding.RuleId))
                    .GroupBy(finding =>
                        string.Concat(
                            finding.RuleId.Trim().ToLowerInvariant(),
                            "|",
                            finding.FindingType.Trim().ToLowerInvariant()));

        List<(
            AccessibilityTopFindingViewModel Finding,
            int Priority)> rankedFindings =
                new();

        foreach (IGrouping<string, AuthenticatedFindingSnapshot>
            findingGroup in findingGroups)
        {
            List<(
                AuthenticatedFindingSnapshot Finding,
                AuthenticatedRunSnapshot Run)> occurrences =
                    new();

            foreach (AuthenticatedFindingSnapshot finding
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

                occurrences.Add((finding, run));
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
                                occurrence.Run.ApplicationName.Trim())
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
            finding.Impact?.Trim().ToLowerInvariant() switch
            {
                "critical" => 0,
                "serious" => 3,
                "moderate" => 6,
                "minor" => 9,
                _ => 12
            };

        int wcagRank =
            finding.WcagLevel?.Trim().ToUpperInvariant() switch
            {
                "A" => 1,
                "AA" => 2,
                _ => 3
            };

        return severityRank + wcagRank;
    }

    private static List<AccessibilityTrendPointViewModel>
    BuildTrendPoints(
        IReadOnlyCollection<AuthenticatedStepSnapshot> steps,
        IReadOnlyCollection<AuthenticatedFindingSnapshot> findings,
        IReadOnlyCollection<PublicScanSnapshot> publicScans)
    {
        Dictionary<int, DateTime> stepDatesById =
            steps.ToDictionary(
                step => step.Id,
                step => step.ScannedAt.Date);

        HashSet<DateTime> reportingDates =
            steps
                .Select(step => step.ScannedAt.Date)
                .Concat(
                    publicScans.Select(scan =>
                        scan.EffectiveScanDate.Date))
                .Distinct()
                .ToHashSet();

        List<AccessibilityTrendPointViewModel> trends =
            new();

        foreach (DateTime reportingDate
            in reportingDates.OrderBy(date => date))
        {
            List<AuthenticatedFindingSnapshot> findingsForDate =
                findings
                    .Where(finding =>
                        stepDatesById.TryGetValue(
                            finding.AuthenticatedAuditStepId,
                            out DateTime findingDate) &&
                        findingDate == reportingDate)
                    .ToList();

            List<PublicScanSnapshot> publicScansForDate =
                publicScans
                    .Where(scan =>
                        scan.EffectiveScanDate.Date ==
                        reportingDate)
                    .ToList();

            trends.Add(
                new AccessibilityTrendPointViewModel
                {
                    Date =
                        reportingDate,

                    AuthenticatedFindings =
                        findingsForDate.Count(IsViolation),

                    AuthenticatedStatesScanned =
                        steps.Count(step =>
                            step.ScannedAt.Date ==
                            reportingDate),

                    FixFirstFindings =
                        findingsForDate.Count(IsFixFirst),

                    PublicPagesScanned =
                        publicScansForDate.Count,

                    WaveErrorsAndContrastErrors =
                        publicScansForDate
                            .Where(scan =>
                                scan.WaveScanSucceeded)
                            .Sum(scan =>
                                scan.WaveErrors +
                                scan.WaveContrastErrors)
                });
        }

        return trends;
    }

    private static AccessibilityOverviewSummaryViewModel
        BuildSummary(
            IReadOnlyCollection<AuthenticatedRunSnapshot> runs,
            IReadOnlyCollection<AuthenticatedStepSnapshot> steps,
            IReadOnlyCollection<AuthenticatedFindingSnapshot> findings,
            IReadOnlyCollection<PublicScanSnapshot> publicScans,
            string? severity,
            string? wcagLevel,
            string? findingType)
    {
        bool hasFindingFilters =
            severity is not null ||
            wcagLevel is not null ||
            findingType is not null;

        List<AuthenticatedFindingSnapshot> violationFindings =
            findings
                .Where(IsViolation)
                .ToList();

        int totalAutomatedFindings =
            hasFindingFilters
                ? violationFindings.Count
                : steps.Sum(step =>
                    step.ViolationRuleCount);

        int totalAffectedElements =
            hasFindingFilters
                ? violationFindings.Sum(finding =>
                    finding.AffectedElementCount)
                : steps.Sum(step =>
                    step.AffectedElementCount);

        int authenticatedStatesWithFindings;

        if (hasFindingFilters)
        {
            authenticatedStatesWithFindings =
                findings
                    .Select(finding =>
                        finding.AuthenticatedAuditStepId)
                    .Distinct()
                    .Count();
        }
        else
        {
            authenticatedStatesWithFindings =
                steps.Count(step =>
                    step.ViolationRuleCount > 0 ||
                    step.NeedsReviewRuleCount > 0);
        }

        int fixFirstFindings =
            findings.Count(IsFixFirst);

        int publicPagesWithFindings =
            publicScans.Count(scan =>
                scan.WaveScanSucceeded &&
                (scan.WaveErrors > 0 ||
                 scan.WaveContrastErrors > 0 ||
                 scan.WaveAlerts > 0));

        DateTime? latestAuthenticatedDate = null;

        if (steps.Count > 0)
        {
            latestAuthenticatedDate =
                steps.Max(step => step.ScannedAt);
        }
        else if (runs.Count > 0)
        {
            latestAuthenticatedDate =
                runs.Max(run => run.StartedAt);
        }

        DateTime? latestPublicDate =
            publicScans.Count > 0
                ? publicScans.Max(scan =>
                    scan.EffectiveScanDate)
                : null;

        DateTime? latestAuditDate = null;
        string? latestAuditSource = null;

        if (latestAuthenticatedDate.HasValue &&
            latestPublicDate.HasValue)
        {
            if (latestAuthenticatedDate.Value >
                latestPublicDate.Value)
            {
                latestAuditDate =
                    latestAuthenticatedDate;

                latestAuditSource =
                    "Authenticated axe-core";
            }
            else if (latestPublicDate.Value >
                     latestAuthenticatedDate.Value)
            {
                latestAuditDate =
                    latestPublicDate;

                latestAuditSource =
                    "Public WAVE";
            }
            else
            {
                latestAuditDate =
                    latestAuthenticatedDate;

                latestAuditSource =
                    "Public and authenticated";
            }
        }
        else if (latestAuthenticatedDate.HasValue)
        {
            latestAuditDate =
                latestAuthenticatedDate;

            latestAuditSource =
                "Authenticated axe-core";
        }
        else if (latestPublicDate.HasValue)
        {
            latestAuditDate =
                latestPublicDate;

            latestAuditSource =
                "Public WAVE";
        }

        return new AccessibilityOverviewSummaryViewModel
        {
            ApplicationsAudited =
                runs
                    .Select(run =>
                        run.ApplicationName.Trim())
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .Count(),

            PublicPagesScanned =
                publicScans.Count,

            SuccessfulWaveScans =
                publicScans.Count(scan =>
                    scan.WaveScanSucceeded),

            AuthenticatedStatesScanned =
                steps.Count,

            TotalAutomatedFindings =
                totalAutomatedFindings,

            TotalAffectedElements =
                totalAffectedElements,

            FixFirstFindings =
                fixFirstFindings,

            PublicPagesWithFindings =
                publicPagesWithFindings,

            AuthenticatedStatesWithFindings =
                authenticatedStatesWithFindings,

            LatestAuditDate =
                latestAuditDate,

            LatestAuditSource =
                latestAuditSource
        };
    }

    private static AccessibilityHealthViewModel BuildHealth(
        IReadOnlyCollection<AuthenticatedStepSnapshot> steps,
        IReadOnlyCollection<AuthenticatedFindingSnapshot> findings,
        IReadOnlyCollection<PublicScanSnapshot> publicScans)
    {
        int passedRuleResults =
            steps.Sum(step =>
                step.PassedRuleCount);

        int violationRuleResults =
            steps.Sum(step =>
                step.ViolationRuleCount);

        int needsReviewRuleResults =
            steps.Sum(step =>
                step.NeedsReviewRuleCount);

        int totalRuleResults =
            passedRuleResults +
            violationRuleResults +
            needsReviewRuleResults;

        double automatedCheckPassRate =
            totalRuleResults == 0
                ? 0
                : Math.Round(
                    passedRuleResults * 100d /
                    totalRuleResults,
                    1);

        List<PublicScanSnapshot> successfulWaveScans =
            publicScans
                .Where(scan =>
                    scan.WaveScanSucceeded)
                .ToList();

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
                        "AA")),

            SuccessfulWaveScans =
                successfulWaveScans.Count,

            WaveErrors =
                successfulWaveScans.Sum(scan =>
                    scan.WaveErrors),

            WaveContrastErrors =
                successfulWaveScans.Sum(scan =>
                    scan.WaveContrastErrors),

            WaveAlerts =
                successfulWaveScans.Sum(scan =>
                    scan.WaveAlerts),

            WaveFeatures =
                successfulWaveScans.Sum(scan =>
                    scan.WaveFeatures),

            WaveAria =
                successfulWaveScans.Sum(scan =>
                    scan.WaveAria),

            PublicPagesWithoutWaveErrorsOrContrastErrors =
                successfulWaveScans.Count(scan =>
                    scan.WaveErrors == 0 &&
                    scan.WaveContrastErrors == 0)
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
        return IsImpact(impact, "Critical") ||
               IsImpact(impact, "Serious") ||
               IsImpact(impact, "Moderate") ||
               IsImpact(impact, "Minor");
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

    private static string NormalizeAuditSource(
        string? auditSource)
    {
        if (string.Equals(
            auditSource,
            "Public",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Public";
        }

        if (string.Equals(
            auditSource,
            "Authenticated",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Authenticated";
        }

        return "All";
    }

    private static string? NormalizeSeverity(
        string? severity)
    {
        string? normalizedValue =
            NormalizeOptionalValue(severity)?
                .ToLowerInvariant();

        return normalizedValue switch
        {
            "critical" => "Critical",
            "serious" => "Serious",
            "moderate" => "Moderate",
            "minor" => "Minor",
            "unknown" => "Unknown",
            _ => null
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
            "A" => "A",
            "AA" => "AA",
            "UNMAPPED" => "Unmapped",
            _ => null
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
            "violation" => "Violation",
            "needsreview" => "NeedsReview",
            "needs review" => "NeedsReview",
            _ => null
        };
    }

    private static string? NormalizeOptionalValue(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
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

    private sealed class PublicScanSnapshot
    {
        public int Id { get; init; }

        public string Url { get; init; }
            = string.Empty;

        public DateTime DateScanned { get; init; }

        public DateTime? WaveScannedAt { get; init; }

        public bool WaveScanSucceeded { get; init; }

        public int WaveErrors { get; init; }

        public int WaveContrastErrors { get; init; }

        public int WaveAlerts { get; init; }

        public int WaveFeatures { get; init; }

        public int WaveAria { get; init; }

        public string? WaveErrorMessage { get; init; }

        public DateTime EffectiveScanDate =>
            WaveScannedAt ?? DateScanned;
    }
}
