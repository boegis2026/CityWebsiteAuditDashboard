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

    /*
    * Progress is kept only in memory for the active browser session.
    * It is not saved as audit history in SQL Server.
    */
    public AuthenticatedAuditProgressResult Progress { get; set; }
        = new()
        {
            IsScanning = false,
            Stage = "Idle",
            StagePercent = 0,
            CurrentPageNumber = 0,
            TotalPageCount = null
        };

    public ValueTask DisposeAsync()
    {
        /*
         * Default cleanup path for situations where the browser has not
         * already been closed through StopSessionAsync.
         */
        return DisposeAsync(closeBrowser: true);
    }

    public async ValueTask DisposeAsync(bool closeBrowser)
    {
        if (closeBrowser)
        {
            try
            {
                if (Browser is not null &&
                    Browser.IsConnected)
                {
                    await Browser.CloseAsync();
                }
            }
            catch (PlaywrightException)
            {
                // The browser may already be closed or disconnected.
            }
            finally
            {
                Playwright?.Dispose();
                OperationLock.Dispose();
            }

            return;
        }

        /*
         * StopSessionAsync already closed the browser through CDP.
         *
         * Playwright.Dispose takes approximately 30 seconds on this
         * workstation, so complete that driver cleanup in the background
         * instead of blocking the dashboard request.
         */
        IPlaywright? playwrightToDispose = Playwright;

        OperationLock.Dispose();

        _ = Task.Run(() =>
        {
            try
            {
                playwrightToDispose?.Dispose();
            }
            catch
            {
                // Background cleanup must not affect the completed audit run.
            }
        });
    }
}