namespace CityWebsiteAuditDashboard.ViewModels;

/// <summary>
/// Contains the authenticated audit runs displayed on the history page.
/// </summary>
public sealed class AuthenticatedAuditHistoryViewModel
{
    public IReadOnlyList<AuthenticatedAuditRunSummaryViewModel> Runs { get; init; }
        = Array.Empty<AuthenticatedAuditRunSummaryViewModel>();
}

/// <summary>
/// Summary information for one authenticated audit run.
///
/// Detailed step results will be displayed on a separate details page.
/// </summary>
public sealed class AuthenticatedAuditRunSummaryViewModel
{
    public int Id { get; init; }

    public string ApplicationName { get; init; } = string.Empty;

    public string StartingUrl { get; init; } = string.Empty;

    public string AccessibilityEngine { get; init; } = string.Empty;

    public DateTime StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public string Status { get; init; } = string.Empty;

    public int StepCount { get; init; }

    public int SuccessfulStepCount { get; init; }

    public int FailedStepCount { get; init; }

    public int? FinalStepNumber { get; init; }

    public string? ErrorMessage { get; init; }
}
