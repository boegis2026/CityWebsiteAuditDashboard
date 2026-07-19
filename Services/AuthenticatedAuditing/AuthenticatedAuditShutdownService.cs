namespace CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

/// <summary>
/// Requests cleanup of live authenticated audit sessions when ASP.NET Core
/// performs a graceful application shutdown.
///
/// AuthenticatedAuditStartupRecoveryService remains the backup for crashes,
/// forced termination, or workstation restarts where graceful cleanup cannot
/// execute.
/// </summary>
public sealed class AuthenticatedAuditShutdownService : IHostedService
{
    private readonly IAuthenticatedAuditService _authenticatedAuditService;
    private readonly ILogger<AuthenticatedAuditShutdownService> _logger;

    public AuthenticatedAuditShutdownService(
        IAuthenticatedAuditService authenticatedAuditService,
        ILogger<AuthenticatedAuditShutdownService> logger)
    {
        _authenticatedAuditService = authenticatedAuditService;
        _logger = logger;
    }

    public Task StartAsync(
        CancellationToken cancellationToken)
    {
        // No startup work is required. Startup recovery is handled separately.
        return Task.CompletedTask;
    }

    public async Task StopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _authenticatedAuditService.InterruptAllSessionsAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Application shutdown ended before every authenticated " +
                "browser session could be cleaned up.");
        }
        catch (Exception exception)
        {
            /*
             * Shutdown cleanup errors are logged rather than rethrown because
             * they should not prevent the web application from terminating.
             * Startup recovery will fix any remaining Running database rows.
             */
            _logger.LogError(
                exception,
                "An error occurred while closing authenticated audit sessions " +
                "during application shutdown.");
        }
    }
}
