using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.Models;

public class AuthenticatedAuditStep
{
    public int Id { get; set; }

    public int AuthenticatedAuditRunId { get; set; }

    public int StepNumber { get; set; }

    [Required]
    [StringLength(200)]
    public string StepName { get; set; } = string.Empty;

    [Required]
    [StringLength(2048)]
    public string Url { get; set; } = string.Empty;

    [StringLength(500)]
    public string? PageTitle { get; set; }

    [StringLength(500)]
    public string? Heading { get; set; }

    [StringLength(128)]
    public string? DomFingerprint { get; set; }

    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    public int VisibleFormCount { get; set; }

    public int VisibleFieldCount { get; set; }

    public int VisibleButtonCount { get; set; }

    public int ViolationRuleCount { get; set; }

    public int AffectedElementCount { get; set; }

    public int NeedsReviewRuleCount { get; set; }

    public int PassedRuleCount { get; set; }

    public bool ScanSucceeded { get; set; }

    // This records that the authenticated workflow ended at this state.
    // It does not mean the audit tool clicked Submit, Pay, or Finalize.
    public bool WasFinalStep { get; set; }

    [StringLength(4000)]
    public string? ErrorMessage { get; set; }

    public AuthenticatedAuditRun AuthenticatedAuditRun { get; set; } = null!;

    // Stores the individual violation and needs review rules reported for this
    // rendered state. Passed rules are represented only by PassedRuleCount.
    public ICollection<AuthenticatedAuditFinding> Findings { get; set; }
        = new List<AuthenticatedAuditFinding>();
}
