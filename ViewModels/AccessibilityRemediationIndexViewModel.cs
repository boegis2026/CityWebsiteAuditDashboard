namespace CityWebsiteAuditDashboard.ViewModels;

public sealed class AccessibilityRemediationIndexViewModel
{
    public List<AccessibilityRemediationListItemViewModel> Items { get; set; }
        = new();
}

public sealed class AccessibilityRemediationListItemViewModel
{
    public int Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string ApplicationName { get; set; } = string.Empty;

    public string StepName { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string FindingType { get; set; } = string.Empty;

    public string RuleId { get; set; } = string.Empty;

    public string? Impact { get; set; }

    public string? WcagLevel { get; set; }

    public int AffectedElementCount { get; set; }

    public DateTime DetectedAt { get; set; }
}
