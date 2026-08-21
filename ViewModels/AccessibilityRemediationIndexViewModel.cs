namespace CityWebsiteAuditDashboard.ViewModels;

public sealed class AccessibilityRemediationIndexViewModel
{
    public string? StatusFilter { get; set; }

    public string? ApplicationFilter { get; set; }

    public string? SeverityFilter { get; set; }

    public string? AssigneeFilter { get; set; }

    public List<string> ApplicationOptions { get; set; } = new();

    public List<string> AssigneeOptions { get; set; } = new();

    public int TotalCount { get; set; }

    public int OpenCount { get; set; }

    public int InProgressCount { get; set; }

    public int FixedCount { get; set; }

    public int VerifiedCount { get; set; }

    public int WontFixCount { get; set; }

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
