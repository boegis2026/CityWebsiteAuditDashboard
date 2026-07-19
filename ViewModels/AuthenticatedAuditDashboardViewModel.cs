using System.ComponentModel.DataAnnotations;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

namespace CityWebsiteAuditDashboard.ViewModels;

/// <summary>
/// Information displayed on the authenticated-audit dashboard page.
/// </summary>
public sealed class AuthenticatedAuditDashboardViewModel
{
    public AuthenticatedAuditStartInputModel StartInput { get; set; }
        = new();

    public Guid? SessionId { get; set; }

    public string? ApplicationName { get; set; }

    public string? StartingUrl { get; set; }

    public string AccessibilityEngine { get; set; } = "axe-core";

    public DateTime? StartedAt { get; set; }

    public AuthenticatedAuditStepResult? LastStepResult { get; set; }

    public string? StatusMessage { get; set; }

    /*
     * This property controls what the dashboard displays. The live browser
     * itself remains inside AuthenticatedAuditService and is never placed
     * directly in a controller or view model.
     */
    public bool SessionIsActive =>
        SessionId.HasValue && SessionId.Value != Guid.Empty;
}

/// <summary>
/// User entered information required to launch the browser.
/// </summary>
public sealed class AuthenticatedAuditStartInputModel
{
    [Required(ErrorMessage = "Enter an application name.")]
    [StringLength(
        200,
        ErrorMessage = "The application name cannot exceed 200 characters.")]
    [Display(Name = "Application name")]
    public string ApplicationName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a starting URL.")]
    [StringLength(
        2048,
        ErrorMessage = "The starting URL cannot exceed 2,048 characters.")]
    [Url(ErrorMessage = "Enter a valid HTTP or HTTPS URL.")]
    [Display(Name = "Starting URL")]
    public string StartingUrl { get; set; } = string.Empty;
}

/// <summary>
/// Values posted back by the Scan and Stop forms.
///
/// Application information is repeated in hidden fields only so the page can
/// continue displaying it after each POST. The session GUID is what the
/// service uses to locate the live browser.
/// </summary>
public sealed class AuthenticatedAuditSessionInputModel
{
    public Guid SessionId { get; set; }

    public string ApplicationName { get; set; } = string.Empty;

    public string StartingUrl { get; set; } = string.Empty;

    public string AccessibilityEngine { get; set; } = "axe-core";

    public DateTime? StartedAt { get; set; }

    [Display(Name = "Mark the last scanned state as final")]
    public bool MarkLastStepAsFinal { get; set; } = true;
}
