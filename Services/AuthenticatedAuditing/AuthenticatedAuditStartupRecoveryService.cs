using CityWebsiteAuditDashboard.Data;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

/// <summary>
/// Marks authenticated audit runs left in the Running state as Interrupted
/// whenever the dashboard application starts.
///
/// Live Playwright browser objects exist only in application memory. After an
/// application restart, those old sessions cannot be resumed safely.
/// </summary>
public sealed class AuthenticatedAuditStartupRecoveryService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuthenticatedAuditStartupRecoveryService> _logger;

    public AuthenticatedAuditStartupRecoveryService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuthenticatedAuditStartupRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope =
            _scopeFactory.CreateAsyncScope();

        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        List<Models.AuthenticatedAuditRun> abandonedRuns =
            await dbContext.AuthenticatedAuditRuns
                .Where(run => run.Status == "Running")
                .ToListAsync(cancellationToken);

        if (abandonedRuns.Count == 0)
        {
            return;
        }

        DateTime interruptedAt = DateTime.UtcNow;

        foreach (Models.AuthenticatedAuditRun run in abandonedRuns)
        {
            run.Status = "Interrupted";
            run.CompletedAt = interruptedAt;

            run.ErrorMessage =
                "The dashboard application stopped or restarted before this " +
                "authenticated audit session was completed. The in-memory " +
                "Playwright browser session is no longer available.";
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Marked {RunCount} abandoned authenticated audit run(s) as interrupted.",
            abandonedRuns.Count);
    }

    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
