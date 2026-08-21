namespace CityWebsiteAuditDashboard.ViewModels;

public sealed class AccessibilityRemediationDetailsViewModel
{
    public int Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string ApplicationName { get; set; } = string.Empty;

    public int AuditRunId { get; set; }

    public int StepNumber { get; set; }

    public string StepName { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? PageTitle { get; set; }

    public string? Heading { get; set; }

    public DateTime DetectedAt { get; set; }

    public string FindingType { get; set; } = string.Empty;

    public string RuleId { get; set; } = string.Empty;

    public string? Impact { get; set; }

    public string? WcagLevel { get; set; }

    public string? WcagTags { get; set; }

    public string? Help { get; set; }

    public string? Description { get; set; }

    public string? HelpUrl { get; set; }

    public int AffectedElementCount { get; set; }

    public List<AccessibilityRemediationNodeViewModel> Nodes { get; set; }
        = new();

    public List<AccessibilityRemediationHistoryViewModel> History { get; set; }
        = new();
}

public sealed class AccessibilityRemediationNodeViewModel
{
    public string? Target { get; set; }

    public string? Html { get; set; }

    public string? FailureSummary { get; set; }

    public string? ElementFixGuidance { get; set; }
}

public sealed class AccessibilityRemediationHistoryViewModel
{
    public string EventType { get; set; } = string.Empty;

    public string? PreviousStatus { get; set; }

    public string? NewStatus { get; set; }

    public string? PreviousAssignee { get; set; }

    public string? NewAssignee { get; set; }

    public string? Notes { get; set; }

    public DateTime ChangedAt { get; set; }

    public string? ChangedBy { get; set; }
}
