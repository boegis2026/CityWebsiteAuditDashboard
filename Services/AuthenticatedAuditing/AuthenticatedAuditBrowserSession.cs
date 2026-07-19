using Microsoft.Playwright;

namespace CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

/// <summary>
/// Represents one live, manually authenticated Playwright browser session.
///
/// This class is intentionally internal because controllers should interact
/// with IAuthenticatedAuditService rather than directly controlling Playwright.
/// </summary>
internal sealed class AuthenticatedAuditBrowserSession : IAsyncDisposable
{
    /// <summary>
    /// Public identifier used by the dashboard to reference this live session.
    /// </summary>
    public required Guid SessionId { get; init; }

    /// <summary>
    /// Database ID of the related AuthenticatedAuditRun record.
    /// </summary>
    public required int AuditRunId { get; init; }

    public required string ApplicationName { get; init; }

    public required string StartingUrl { get; init; }

    public string AccessibilityEngine { get; init; } = "axe-core";

    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    public required IPlaywright Playwright { get; init; }

    public required IBrowser Browser { get; init; }

    public required IBrowserContext BrowserContext { get; init; }

    /// <summary>
    /// The page currently selected for auditing.
    ///
    /// This may change because the protected BOE workflow can open the
    /// application in another browser tab after authentication.
    /// </summary>
    public IPage? ActivePage { get; set; }

    /// <summary>
    /// Number assigned to the next rendered state that is scanned.
    /// </summary>
    public int NextStepNumber { get; set; } = 1;

    /// <summary>
    /// Database ID of the most recently saved step.
    ///
    /// When the user stops the session, this allows the service to mark the
    /// last scanned state as final without clicking Submit, Pay, or Finish.
    /// </summary>
    public int? LastSavedStepId { get; set; }

    /// <summary>
    /// Indicates that the service has started shutting down this session.
    ///
    /// A queued dashboard request must not scan the browser after shutdown
    /// has begun, even if it obtained the session reference earlier.
    /// </summary>
    public bool IsStopping { get; set; }

    /// <summary>
    /// Prevents two dashboard requests from scanning or stopping the same
    /// browser session simultaneously.
    /// </summary>
    public SemaphoreSlim OperationLock { get; } = new(1, 1);

    public async ValueTask DisposeAsync()
    {
        // Attempt each cleanup operation independently. If one Playwright
        // object is already closed, the remaining resources should still
        // receive their cleanup calls.
        try
        {
            await BrowserContext.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // The context may already have been closed by the browser or user.
        }

        try
        {
            await Browser.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // The browser may already have been manually closed.
        }

        Playwright.Dispose();
     /*
    * OperationLock is intentionally not disposed here.
    *
    * Another request may have obtained the session immediately before it was
    * removed from the session dictionary and may still be waiting on this lock.
    * That request will acquire the lock, see IsStopping, and exit safely.
    */
    }
}
