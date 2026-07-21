namespace CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

/// <summary>
/// Controls one manually authenticated browser session.
///
/// The browser must remain open between scans because the user logs in
/// and navigates through the protected application manually.
/// </summary>
public interface IAuthenticatedAuditService
{
    /// <summary>
    /// Returns the currently active authenticated browser session, if one exists.
    ///
    /// This allows the dashboard to restore the Scan and Stop controls after a
    /// page refresh or after the user navigates to audit history and returns.
    /// </summary>
    AuthenticatedAuditSessionResult? GetActiveSession();

    /// <summary>
    /// Opens a headed Playwright browser and begins a new audit session.
    /// </summary>
    /// 

    Task<AuthenticatedAuditSessionResult> StartSessionAsync(
        AuthenticatedAuditStartRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans the page state that is currently visible in the authenticated browser.
    ///
    /// This method must not click Next, Submit, Pay, Finish, or other workflow
    /// controls. Navigation remains under the user's control.
    /// </summary>
    Task<AuthenticatedAuditStepResult> ScanCurrentStepAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Automatically visits and scans a list of protected URLs using the same
    /// authenticated Playwright browser context.
    ///
    /// The user signs in once before starting the batch. Authentication cookies
    /// and session storage remain available for every URL in the batch.
    /// </summary>
    Task<AuthenticatedAuditBatchResult> ScanBatchAsync(
        Guid sessionId,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Safely closes the browser and ends the audit session.
    /// </summary>
    Task StopSessionAsync(
        Guid sessionId,
        bool markLastStepAsFinal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrupts and closes every live browser session when the dashboard
    /// application is shutting down.
    ///
    /// Controllers should not call this operation. It is intended for the
    /// application's hosted shutdown service.
    /// </summary>
    Task InterruptAllSessionsAsync(
        CancellationToken cancellationToken = default);
}
