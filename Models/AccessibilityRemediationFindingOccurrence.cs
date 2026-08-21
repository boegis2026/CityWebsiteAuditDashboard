using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.Models;

public sealed class AccessibilityRemediationFindingOccurrence
{
    public int Id { get; set; }

    public int AccessibilityRemediationItemId { get; set; }

    public int AuthenticatedAuditFindingId { get; set; }

    [StringLength(100)]
    public string? MatchMethod { get; set; }

    public decimal? MatchConfidence { get; set; }

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    [StringLength(200)]
    public string? LinkedBy { get; set; }

    public AccessibilityRemediationItem RemediationItem { get; set; } = null!;

    public AuthenticatedAuditFinding AuthenticatedAuditFinding { get; set; }
        = null!;
}
