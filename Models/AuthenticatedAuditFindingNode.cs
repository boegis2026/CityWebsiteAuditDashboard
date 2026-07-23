namespace CityWebsiteAuditDashboard.Models;

public class AuthenticatedAuditFindingNode
{
    public int Id { get; set; }

    public int AuthenticatedAuditFindingId { get; set; }

    // CSS selector or axe target identifying the affected page element.
    public string Target { get; set; } = string.Empty;

    public string? Html { get; set; }

    public string? FailureSummary { get; set; }

    public AuthenticatedAuditFinding AuthenticatedAuditFinding { get; set; }
        = null!;
}
