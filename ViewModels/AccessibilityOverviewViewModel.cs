namespace CityWebsiteAuditDashboard.ViewModels;

/// <summary>
/// Contains all read-only reporting data displayed on the
/// Accessibility Overview page.
/// </summary>
public sealed class AccessibilityOverviewViewModel
{
    public AccessibilityOverviewFilterViewModel Filters { get; init; }
        = new();

    public IReadOnlyList<string> ApplicationOptions { get; init; }
        = Array.Empty<string>();

    public AccessibilityOverviewSummaryViewModel Summary { get; init; }
        = new();

    public AccessibilityHealthViewModel Health { get; init; }
        = new();

    public AccessibilityIssueBreakdownViewModel IssueBreakdown { get; init; }
        = new();

    public IReadOnlyList<AccessibilityTrendPointViewModel> Trends { get; init; }
        = Array.Empty<AccessibilityTrendPointViewModel>();

    public IReadOnlyList<AccessibilityApplicationRankingViewModel>
        Applications
    { get; init; }
        = Array.Empty<AccessibilityApplicationRankingViewModel>();

    public IReadOnlyList<AccessibilityTopFindingViewModel>
        TopFindings
    { get; init; }
        = Array.Empty<AccessibilityTopFindingViewModel>();

    public bool HasAnyData =>
        Summary.AuthenticatedStatesScanned > 0;
}

/// <summary>
/// Filters selected by the user on the reporting dashboard.
/// </summary>
public sealed class AccessibilityOverviewFilterViewModel
{
    public string? ApplicationName { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string? Severity { get; set; }

    public string? WcagLevel { get; set; }

    public string? FindingType { get; set; }

    public bool LatestOnly { get; set; } = true;
}

/// <summary>
/// Management-facing totals shown in the top summary cards.
/// </summary>
public sealed class AccessibilityOverviewSummaryViewModel
{
    public int ApplicationsAudited { get; init; }

    public int AuthenticatedStatesScanned { get; init; }

    public int TotalAutomatedFindings { get; init; }

    public int TotalAffectedElements { get; init; }

    public int FixFirstFindings { get; init; }

    public int PublicPagesWithFindings { get; init; }

    public int AuthenticatedStatesWithFindings { get; init; }

}

/// <summary>
/// Automated check-pass calculation and related WCAG summaries.
/// </summary>
public sealed class AccessibilityHealthViewModel
{
    public int PassedRuleResults { get; init; }

    public int ViolationRuleResults { get; init; }

    public int NeedsReviewRuleResults { get; init; }

    public int TotalRuleResults { get; init; }

    public double AutomatedCheckPassRate { get; init; }

    public int WcagLevelAFindingCount { get; init; }

    public int WcagLevelAAFindingCount { get; init; }

    public int BestPracticeOrUnmappedFindingCount { get; init; }

    public bool HasAuthenticatedRuleData =>
        TotalRuleResults > 0;
}

/// <summary>
/// Counts used by the issue severity and WCAG breakdown sections.
/// </summary>
public sealed class AccessibilityIssueBreakdownViewModel
{
    public int Critical { get; init; }

    public int Serious { get; init; }

    public int Moderate { get; init; }

    public int Minor { get; init; }

    public int UnknownSeverity { get; init; }

    public int NeedsManualReview { get; init; }

    public int WcagLevelA { get; init; }

    public int WcagLevelAA { get; init; }

    public int BestPracticeOrUnmapped { get; init; }
}

/// <summary>
/// One reporting period displayed in the trends section.
/// </summary>
public sealed class AccessibilityTrendPointViewModel
{
    public DateTime Date { get; init; }

    public int AuthenticatedFindings { get; init; }

    public int AuthenticatedStatesScanned { get; init; }

    public int FixFirstFindings { get; init; }
}

/// <summary>
/// Latest authenticated audit summary for one application.
/// </summary>
public sealed class AccessibilityApplicationRankingViewModel
{
    public string ApplicationName { get; init; } = string.Empty;

    public int LatestRunId { get; init; }

    public string StartingUrl { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime LatestAuditDate { get; init; }

    public int StateCount { get; init; }

    public int SuccessfulStateCount { get; init; }

    public int CriticalFindingCount { get; init; }

    public int SeriousFindingCount { get; init; }

    public int FixFirstFindingCount { get; init; }

    public int NeedsReviewFindingCount { get; init; }

    public int AffectedElementCount { get; init; }

    public int TotalFindingCount { get; init; }

    public int? PreviousRunId { get; init; }

    public int? PreviousFindingCount { get; init; }

    public int? PreviousStateCount { get; init; }

    public bool CoverageChanged =>
        PreviousStateCount.HasValue &&
        PreviousStateCount.Value != StateCount;

    public int? FindingCountChange =>
        PreviousFindingCount.HasValue
            ? TotalFindingCount - PreviousFindingCount.Value
            : null;
}

/// <summary>
/// Aggregated axe-core rule displayed in the Top Findings table.
/// </summary>
public sealed class AccessibilityTopFindingViewModel
{
    public string RuleId { get; init; } = string.Empty;

    public string FindingType { get; init; } = string.Empty;

    public string? Impact { get; init; }

    public string? WcagLevel { get; init; }

    public string? Help { get; init; }

    public string? Description { get; init; }

    public string? HelpUrl { get; init; }

    public int ApplicationCount { get; init; }

    public int StateCount { get; init; }

    public int AffectedElementCount { get; init; }

    public int? LatestRunId { get; init; }

    public string? LatestApplicationName { get; init; }
}
