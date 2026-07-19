namespace CityWebsiteAuditDashboard.ViewModels;

/// <summary>
/// Full dashboard details for one authenticated audit run and all of its
/// separately scanned rendered states.
/// </summary>
public sealed class AuthenticatedAuditDetailsViewModel
{
    public int Id { get; init; }

    public string ApplicationName { get; init; } = string.Empty;

    public string StartingUrl { get; init; } = string.Empty;

    public string AccessibilityEngine { get; init; } = string.Empty;

    public DateTime StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<AuthenticatedAuditStepDetailsViewModel> Steps
    { get; init; }
        = Array.Empty<AuthenticatedAuditStepDetailsViewModel>();

    public int SuccessfulStepCount =>
        Steps.Count(step => step.ScanSucceeded);

    public int FailedStepCount =>
        Steps.Count(step => !step.ScanSucceeded);

    public int TotalViolationRuleCount =>
        Steps.Sum(step => step.ViolationRuleCount);

    public int TotalAffectedElementCount =>
        Steps.Sum(step => step.AffectedElementCount);
}

/// <summary>
/// Details for one rendered application state captured during the run.
/// </summary>
public sealed class AuthenticatedAuditStepDetailsViewModel
{
    public int Id { get; init; }

    public int StepNumber { get; init; }

    public string StepName { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string? PageTitle { get; init; }

    public string? Heading { get; init; }

    public string? DomFingerprint { get; init; }

    public DateTime ScannedAt { get; init; }

    public int VisibleFormCount { get; init; }

    public int VisibleFieldCount { get; init; }

    public int VisibleButtonCount { get; init; }

    public int ViolationRuleCount { get; init; }

    public int AffectedElementCount { get; init; }

    public int NeedsReviewRuleCount { get; init; }

    public int PassedRuleCount { get; init; }

    public bool ScanSucceeded { get; init; }

    public bool WasFinalStep { get; init; }

    /// <summary>
    /// Rule-level violations and needs-review results saved for this
    /// rendered state.
    ///
    /// Element HTML and entered form values are intentionally excluded.
    /// </summary>
    public IReadOnlyList<AuthenticatedAuditFindingDetailsViewModel> Findings
    { get; init; }
        = Array.Empty<AuthenticatedAuditFindingDetailsViewModel>();

    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Safe rule-level accessibility information displayed for one scanned state.
/// </summary>
public sealed class AuthenticatedAuditFindingDetailsViewModel
{
    public int Id { get; init; }

    public string FindingType { get; init; } = string.Empty;

    public string RuleId { get; init; } = string.Empty;

    public string? Impact { get; init; }

    public string? Help { get; init; }

    public string? Description { get; init; }

    public string? HelpUrl { get; init; }

    public int AffectedElementCount { get; init; }
}
