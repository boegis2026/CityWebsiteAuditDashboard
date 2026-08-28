using CityWebsiteAuditDashboard.Models;

namespace CityWebsiteAuditDashboard.ViewModels;

public sealed class AccessibilityWorkflowRetestViewModel
{
    public int? OriginalAuditRunId { get; set; }

    public int? RetestAuditRunId { get; set; }

    public IReadOnlyList<AccessibilityWorkflowRetestRunOptionViewModel>
        AuditRuns
    { get; init; }
            = Array.Empty<
                AccessibilityWorkflowRetestRunOptionViewModel>();

    public AccessibilityWorkflowRetestComparisonViewModel?
        Comparison
    { get; init; }

    public IReadOnlyList<AccessibilityWorkflowRetestRunOptionViewModel>
        EligibleRetestRuns
    { get; init; }
        = Array.Empty<
            AccessibilityWorkflowRetestRunOptionViewModel>();
}

public sealed class AccessibilityWorkflowRetestRunOptionViewModel
{
    public int Id { get; init; }

    public string ApplicationName { get; init; }
        = string.Empty;

    public DateTime StartedAt { get; init; }

    public string Status { get; init; }
        = string.Empty;

    public int StepCount { get; init; }
}

public sealed class AccessibilityWorkflowRetestComparisonViewModel
{
    public int OriginalAuditRunId { get; init; }

    public int RetestAuditRunId { get; init; }

    public string ApplicationName { get; init; }
        = string.Empty;

    public bool FormalRetestAlreadyApplied { get; init; }

    public DateTime OriginalAuditStartedAt { get; init; }

    public DateTime RetestAuditStartedAt { get; init; }

    public string RetestAuditStatus { get; init; }
        = string.Empty;

    public int TotalTracked { get; init; }

    public int StillDetected { get; init; }

    public int NotDetected { get; init; }

    public int Inconclusive { get; init; }

    public int Failed { get; init; }

    public IReadOnlyList<
        AccessibilityWorkflowRetestComparisonItemViewModel>
        Items
    { get; init; }
            = Array.Empty<
                AccessibilityWorkflowRetestComparisonItemViewModel>();
}

public sealed class AccessibilityWorkflowRetestComparisonItemViewModel
{
    public int RemediationItemId { get; init; }

    public AccessibilityRemediationStatus CurrentStatus { get; init; }

    public string RuleId { get; init; }
        = string.Empty;

    public string FindingType { get; init; }
        = string.Empty;

    public string? Impact { get; init; }

    public int OriginalStepNumber { get; init; }

    public string? OriginalStepName { get; init; }

    public string OriginalUrl { get; init; }
        = string.Empty;

    public int? RetestStepNumber { get; init; }

    public string? RetestStepName { get; init; }

    public string? RetestUrl { get; init; }

    public decimal? StateConfidence { get; init; }

    public AccessibilityRemediationRetestResult Result { get; init; }

    public string MatchMethod { get; init; }
        = string.Empty;

    public decimal MatchConfidence { get; init; }

    public string? Message { get; init; }
}
