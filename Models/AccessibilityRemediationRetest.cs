using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.Models;

public sealed class AccessibilityRemediationRetest
{
    public int Id { get; set; }

    public int AccessibilityRemediationItemId { get; set; }

    /*
     * The newly scanned authenticated state used for this retest.
     * Nullable so a failed retest can still be recorded even if a
     * new audit step could not be saved.
     */
    public int? AuthenticatedAuditStepId { get; set; }

    /*
     * When the tracked rule is detected again, this points to the
     * matching finding from the new scan.
     *
     * Null means no matching finding was linked.
     */
    public int? MatchedAuthenticatedAuditFindingId { get; set; }

    public AccessibilityRemediationRetestResult Result { get; set; }
        = AccessibilityRemediationRetestResult.Inconclusive;

    [StringLength(100)]
    public string? MatchMethod { get; set; }

    public decimal? MatchConfidence { get; set; }

    public DateTime RetestedAt { get; set; } = DateTime.UtcNow;

    [StringLength(4000)]
    public string? Notes { get; set; }

    [StringLength(200)]
    public string? RetestedBy { get; set; }

    public AccessibilityRemediationItem RemediationItem { get; set; }
        = null!;

    public AuthenticatedAuditStep? AuthenticatedAuditStep { get; set; }

    public AuthenticatedAuditFinding? MatchedAuthenticatedAuditFinding
    { get; set; }
}