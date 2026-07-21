using System.ComponentModel.DataAnnotations;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.ViewModels;

/// <summary>
/// Collects the URLs for one authenticated batch.
///
/// Blank lines are ignored, surrounding spaces are removed, and the entered
/// count must match the number of non-empty URLs.
/// </summary>
public sealed class AuthenticatedAuditBatchInputModel
{
    /*
    * A session ID does not exist until the Playwright browser session starts.
    * The ScanBatch controller action verifies that this ID matches the currently
    * active authenticated session before performing any navigation.
    */
    public Guid SessionId { get; set; }

    [Required(ErrorMessage = "Enter the number of URLs.")]
    [Range(
        1,
        25,
        ErrorMessage = "Authenticated batches are limited to 25 URLs.")]
    [Display(Name = "Number of URLs")]
    public int? NumberOfUrls { get; set; }

    [Required(ErrorMessage = "Paste at least one protected URL.")]
    [Display(Name = "Protected URLs")]
    public string Urls { get; set; } = string.Empty;
}

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

    /// <summary>
    /// Input used to automatically scan multiple protected URLs through the
    /// currently authenticated Playwright browser session.
    /// </summary>
    public AuthenticatedAuditBatchInputModel BatchInput { get; set; }
        = new();


    public string? StatusMessage { get; set; }

    /// <summary>
    /// Displays the most recently completed authenticated batch after the
    /// Post/Redirect/Get workflow returns to the dashboard.
    /// </summary>
    public AuthenticatedAuditBatchResult? LastBatchResult { get; set; }

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
