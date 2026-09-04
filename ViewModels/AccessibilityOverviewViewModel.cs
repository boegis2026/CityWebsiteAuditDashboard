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

    public AccessibilityRemediationProgressViewModel RemediationProgress
    { get; init; }
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
/// One reporting period displayed in the automated-results trends section.
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
    public string ApplicationName { get; init; }
        = string.Empty;

    public int LatestRunId { get; init; }

    public string StartingUrl { get; init; }
        = string.Empty;

    public string Status { get; init; }
        = string.Empty;

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
            ? TotalFindingCount -
              PreviousFindingCount.Value
            : null;
}

/// <summary>
/// Current remediation workflow progress for tracked accessibility findings.
///
/// These values represent remediation workflow state.
/// They are not ADA or WCAG compliance determinations.
/// </summary>
public sealed class AccessibilityRemediationProgressViewModel
{
    public int TotalTracked { get; init; }

    public int Open { get; init; }

    public int InProgress { get; init; }

    public int AwaitingVerification { get; init; }

    public int Verified { get; init; }

    public int WontFix { get; init; }

    /// <summary>
    /// Formal full-workflow retesting and verification progress.
    /// </summary>
    public AccessibilityFormalWorkflowProgressViewModel
        FormalWorkflow
    { get; init; }
        = new();

    public double VerifiedPercent =>
        TotalTracked > 0
            ? (double)Verified /
              TotalTracked *
              100
            : 0;

    public bool HasData =>
        TotalTracked > 0;

    public IReadOnlyList<
        AccessibilityApplicationRemediationProgressViewModel>
        Applications
    { get; init; }
        = Array.Empty<
            AccessibilityApplicationRemediationProgressViewModel>();

    public IReadOnlyList<
        AccessibilityRemediationTrendPointViewModel>
        Trends
    { get; init; }
        = Array.Empty<
            AccessibilityRemediationTrendPointViewModel>();
}

/// <summary>
/// Management-level summary of saved formal full-workflow retests.
///
/// One formal workflow retest is one completed authenticated audit run that
/// has been applied as retest evidence to tracked remediation items.
/// </summary>
public sealed class AccessibilityFormalWorkflowProgressViewModel
{
    /// <summary>
    /// Number of distinct authenticated audit runs that have been saved as
    /// formal workflow retests within the selected reporting scope.
    /// </summary>
    public int TotalFormalRetestRuns { get; init; }

    /// <summary>
    /// Number of applications that have at least one saved formal
    /// workflow retest within the selected reporting scope.
    /// </summary>
    public int ApplicationsFormallyRetested { get; init; }

    /// <summary>
    /// Latest formal workflow retest for the application has been applied,
    /// but none of its Not Detected findings are currently verified from
    /// that retest evidence.
    /// </summary>
    public int LatestApplied { get; init; }

    /// <summary>
    /// Some, but not all, Not Detected findings from the application's
    /// latest formal workflow retest are currently verified from that
    /// retest evidence.
    /// </summary>
    public int LatestPartiallyVerified { get; init; }

    /// <summary>
    /// Every Not Detected finding from the application's latest formal
    /// workflow retest is currently verified from that retest evidence.
    /// </summary>
    public int LatestFullyVerified { get; init; }

    /// <summary>
    /// Latest saved formal workflow retest time across the selected scope.
    /// </summary>
    public DateTime? LatestFormalRetestAt { get; init; }

    public bool HasData =>
        TotalFormalRetestRuns > 0;

    public IReadOnlyList<
        AccessibilityApplicationFormalWorkflowProgressViewModel>
        Applications
    { get; init; }
        = Array.Empty<
            AccessibilityApplicationFormalWorkflowProgressViewModel>();
}

/// <summary>
/// Latest formal workflow retest status for one application.
/// </summary>
public sealed class AccessibilityApplicationFormalWorkflowProgressViewModel
{
    public string ApplicationName { get; init; }
        = string.Empty;

    public int RetestAuditRunId { get; init; }

    public DateTime RetestedAt { get; init; }

    public int TotalTracked { get; init; }

    public int StillDetected { get; init; }

    public int NotDetected { get; init; }

    public int VerifiedFromThisRetest { get; init; }

    public int Inconclusive { get; init; }

    public int Failed { get; init; }

    public int Reopened { get; init; }

    /// <summary>
    /// Applied, Partially Verified, or Fully Verified.
    /// </summary>
    public string VerificationState { get; init; }
        = "Applied";

    public double VerifiedPassedIssuePercent =>
        NotDetected > 0
            ? (double)VerifiedFromThisRetest /
              NotDetected *
              100
            : 0;
}

/// <summary>
/// Daily remediation lifecycle events.
/// </summary>
public sealed class AccessibilityRemediationTrendPointViewModel
{
    public DateTime Date { get; init; }

    public int Verified { get; init; }

    public int Reopened { get; init; }
}

/// <summary>
/// Current remediation status totals for one application.
/// </summary>
public sealed class AccessibilityApplicationRemediationProgressViewModel
{
    public string ApplicationName { get; init; }
        = string.Empty;

    public int TotalTracked { get; init; }

    public int Open { get; init; }

    public int InProgress { get; init; }

    public int AwaitingVerification { get; init; }

    public int Verified { get; init; }

    public int WontFix { get; init; }

    public int Remaining =>
        Open +
        InProgress +
        AwaitingVerification;

    public double VerifiedPercent =>
        TotalTracked > 0
            ? (double)Verified /
              TotalTracked *
              100
            : 0;
}

/// <summary>
/// Aggregated axe-core rule displayed in the Top Findings table.
/// </summary>
public sealed class AccessibilityTopFindingViewModel
{
    public string RuleId { get; init; }
        = string.Empty;

    public string FindingType { get; init; }
        = string.Empty;

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
