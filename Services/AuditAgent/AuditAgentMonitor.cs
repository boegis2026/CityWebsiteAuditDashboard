using Microsoft.AspNetCore.SignalR;
using CityWebsiteAuditDashboard.Hubs;

namespace CityWebsiteAuditDashboard.Services.AuditAgent;

public sealed class AuditAgentMonitor : BackgroundService
{
    private readonly AuditAgentDispatcher _dispatcher;
    private readonly AuditAgentConnectionRegistry _registry;
    private readonly AuditAgentStore _store;
    private readonly IHubContext<AuditAgentHub> _hub;
    private readonly ILogger<AuditAgentMonitor> _logger;

    public AuditAgentMonitor(AuditAgentDispatcher dispatcher, AuditAgentConnectionRegistry registry,
        AuditAgentStore store, IHubContext<AuditAgentHub> hub, ILogger<AuditAgentMonitor> logger)
    { _dispatcher = dispatcher; _registry = registry; _store = store; _hub = hub; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    foreach (string connection in _registry.TakeExpired(DateTimeOffset.UtcNow))
                    {
                        _dispatcher.Detach(connection);
                        try { await _hub.Clients.Client(connection).SendAsync("LeaseExpired", stoppingToken); }
                        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                        { _logger.LogWarning(ex, "Could not notify an expired Agent."); }
                        try { await _store.DisconnectAsync(connection); }
                        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                        { _logger.LogError(ex, "Expired Agent cleanup will be retried by database recovery."); }
                    }
                    await _store.RecoverDisconnectedAsync();
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                { _logger.LogError(ex, "Agent recovery check failed; it will retry."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

