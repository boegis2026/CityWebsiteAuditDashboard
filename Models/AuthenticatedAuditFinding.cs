using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.Models;

/// <summary>
/// Stores one accessibility rule result found during a rendered state scan.
///
/// Only violations and needs review results are persisted. Passed rules remain
/// summarized by the PassedRuleCount field on AuthenticatedAuditStep so the
/// database does not fill with unnecessary successful result records.
/// </summary>
public sealed class AuthenticatedAuditFinding
{
    public int Id { get; set; }

    public int AuthenticatedAuditStepId { get; set; }

    /// <summary>
    /// Identifies whether this result came from axe's Violations collection
    /// or its Incomplete collection.
    /// </summary>
    [Required]
    [StringLength(50)]
    public string FindingType { get; set; } = string.Empty;

    /// <summary>
    /// The accessibility engine's stable rule identifier, such as
    /// "label" or "color contrast".
    /// </summary>
    [Required]
    [StringLength(200)]
    public string RuleId { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Impact { get; set; }

    [StringLength(500)]
    public string? Help { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(2048)]
    public string? HelpUrl { get; set; }

    public string WcagTags { get; set; } = string.Empty;

    public string? WcagLevel { get; set; }

    /// <summary>
    /// Number of page elements reported under this rule.
    ///
    /// We initially store a rule-level summary rather than full HTML snippets
    /// so user entered permit information is not accidentally persisted.
    /// </summary>
    public int AffectedElementCount { get; set; }

    public AuthenticatedAuditStep AuthenticatedAuditStep { get; set; } = null!;
}
