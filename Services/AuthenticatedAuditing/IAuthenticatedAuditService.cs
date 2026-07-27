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

    /*
    * Inspects the current rendered page without filling fields,
    * clicking buttons, navigating, or saving another audit step.
    */
    Task<AuthenticatedAuditNavigationAnalysisResult>
        AnalyzeCurrentStateAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);

    /*
    * Fills supported fields on the current rendered page.
    * Does not click, navigate, submit, or save an audit step.
    */
    Task<AuthenticatedAuditFieldFillResult>
        FillCurrentStateAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);

    /*
    * Prepares the current rendered state for automatic navigation.
    * It analyzes the page, fills supported fields, and identifies a safe
    * Next action, but does not click or submit anything.
    */
    Task<AuthenticatedAuditAutomaticNavigationResult>
        PreviewAutomaticStepAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);

    /*
     * Fills the current state and clicks one safe Next/Continue action.
     * It does not scan or save a state and does not run a navigation loop.
     */
    Task<AuthenticatedAuditAutomaticNavigationResult>
        AdvanceAutomaticStepAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);

    /*
    * Scans the current rendered state and then attempts one guarded
    * automatic advance. Existing manual scanning remains available.
    */
    Task<AuthenticatedAuditAutomaticCycleResult>
        ScanAndAdvanceAutomaticStepAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);

    /*
    * Repeatedly scans and advances through supported rendered states.
    * Stops safely when no Next action exists, manual input is required,
    * or the maximum number of states is reached.
    *
    * Existing manual scanning remains available.
    */
    Task<AuthenticatedAuditAutomaticRunResult>
        RunAutomaticWorkflowAsync(
            Guid sessionId,
            int maximumStateCount = 25,
            CancellationToken cancellationToken = default);

    /*
    * Requests cancellation of the currently running automatic workflow.
    * Returns false when no automatic workflow is running.
    */
    bool RequestAutomaticWorkflowStop(
        Guid sessionId);

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

    // Returns the live in-memory progress for an active browser session.
    AuthenticatedAuditProgressResult? GetProgress(
        Guid sessionId);
}
