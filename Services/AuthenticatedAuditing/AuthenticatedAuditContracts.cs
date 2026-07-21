namespace CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

/// <summary>
/// Information required to begin a manually authenticated audit.
/// </summary>
public sealed class AuthenticatedAuditStartRequest
{
    public string ApplicationName { get; init; } = string.Empty;

    public string StartingUrl { get; init; } = string.Empty;
}

/// <summary>
/// Basic information returned after the browser session has started.
/// </summary>
public sealed class AuthenticatedAuditSessionResult
{
    // A GUID identifies the live browser session without exposing
    // the Playwright browser or page objects to controllers.
    public Guid SessionId { get; init; }

    /// <summary>
    /// Database identifier for the audit run associated with this live browser.
    /// This allows the dashboard GET page to reload the most recently saved step.
    /// </summary>
    public int AuditRunId { get; init; }

    public string ApplicationName { get; init; } = string.Empty;

    public string StartingUrl { get; init; } = string.Empty;

    public string AccessibilityEngine { get; init; } = "axe-core";

    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Accessibility and rendered-page information captured for one workflow state.
/// </summary>
public sealed class AuthenticatedAuditStepResult
{
    public int StepNumber { get; init; }

    public string StepName { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string? PageTitle { get; init; }

    public string? Heading { get; init; }

    // The protected application can change states without changing its URL.
    // The fingerprint helps identify a meaningful rendered DOM change.
    public string? DomFingerprint { get; init; }

    public DateTime ScannedAt { get; init; } = DateTime.UtcNow;

    public int VisibleFormCount { get; init; }

    public int VisibleFieldCount { get; init; }

    public int VisibleButtonCount { get; init; }

    public int ViolationRuleCount { get; init; }

    public int AffectedElementCount { get; init; }

    public int NeedsReviewRuleCount { get; init; }

    public int PassedRuleCount { get; init; }

    /// <summary>
    /// Rule-level violations and needs-review results found during this scan.
    ///
    /// Full element HTML is intentionally not included because authenticated
    /// forms may contain private or user-entered information.
    /// </summary>
    public IReadOnlyList<AuthenticatedAuditFindingResult> Findings { get; init; }
        = Array.Empty<AuthenticatedAuditFindingResult>();

    public bool ScanSucceeded { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Safe rule level accessibility information returned by the scan service.
///
/// This contains enough information to explain the issue without persisting
/// the affected element's HTML or user entered form values.
/// </summary>
public sealed class AuthenticatedAuditFindingResult
{
    public string FindingType { get; init; } = string.Empty;

    public string RuleId { get; init; } = string.Empty;

    public string? Impact { get; init; }

    public string? Help { get; init; }

    public string? Description { get; init; }

    public string? HelpUrl { get; init; }

    public int AffectedElementCount { get; init; }
}

/// <summary>
/// Summarizes one authenticated batch scan performed inside an existing
/// Playwright browser session.
///
/// Every URL uses the same BrowserContext so the user's authentication cookies
/// and session state remain available throughout the batch.
/// </summary>
public sealed class AuthenticatedAuditBatchResult
{
    public int RequestedCount { get; init; }

    public int SucceededCount { get; init; }

    public int FailedCount { get; init; }

    public IReadOnlyList<AuthenticatedAuditBatchItemResult> Items { get; init; }
        = Array.Empty<AuthenticatedAuditBatchItemResult>();
}

/// <summary>
/// Represents the outcome of scanning one URL in an authenticated batch.
/// Successful pages are also persisted as AuthenticatedAuditStep records.
/// </summary>
public sealed class AuthenticatedAuditBatchItemResult
{
    public string Url { get; init; } = string.Empty;

    public int? StepNumber { get; init; }

    public string? StepName { get; init; }

    public bool Succeeded { get; init; }

    public int ViolationRuleCount { get; init; }

    public int NeedsReviewRuleCount { get; init; }

    public int AffectedElementCount { get; init; }

    public string? ErrorMessage { get; init; }
}
