using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.Models;

public sealed class AccessibilityRemediationHistory
{
    public int Id { get; set; }

    public int AccessibilityRemediationItemId { get; set; }

    [StringLength(100)]
    public string EventType { get; set; } = string.Empty;

    public AccessibilityRemediationStatus? PreviousStatus { get; set; }

    public AccessibilityRemediationStatus? NewStatus { get; set; }

    [StringLength(200)]
    public string? PreviousAssignee { get; set; }

    [StringLength(200)]
    public string? NewAssignee { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    [StringLength(200)]
    public string? ChangedBy { get; set; }

    public AccessibilityRemediationItem RemediationItem { get; set; } = null!;
}
