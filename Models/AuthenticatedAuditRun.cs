using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.Models;

public class AuthenticatedAuditRun
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string ApplicationName { get; set; } = string.Empty;

    [Required]
    [StringLength(2048)]
    public string StartingUrl { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string AccessibilityEngine { get; set; } = "axe-core";

    // Store timestamps in UTC so audit history remains consistent
    // regardless of where the application is running.
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    [Required]
    [StringLength(50)]
    public string Status { get; set; } = "Running";

    [StringLength(4000)]
    public string? ErrorMessage { get; set; }

    // One authenticated run can contain multiple rendered application steps.
    public ICollection<AuthenticatedAuditStep> Steps { get; set; }
        = new List<AuthenticatedAuditStep>();
}
