using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.Models;

public sealed class AccessibilityRemediationItem
{
    public int Id { get; set; }

    public AccessibilityRemediationStatus Status { get; set; }
        = AccessibilityRemediationStatus.Open;

    [StringLength(200)]
    public string? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public ICollection<AccessibilityRemediationHistory> History { get; set; }
        = new List<AccessibilityRemediationHistory>();

    public ICollection<AccessibilityRemediationFindingOccurrence> FindingOccurrences { get; set; }
        = new List<AccessibilityRemediationFindingOccurrence>();

    public ICollection<AccessibilityRemediationRetest> Retests { get; set; }
    = new List<AccessibilityRemediationRetest>();
}
