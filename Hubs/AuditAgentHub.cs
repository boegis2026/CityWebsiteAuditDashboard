using CityWebsiteAuditDashboard.Services.AuditAgent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CityWebsiteAuditDashboard.Hubs;

[Authorize(
    AuthenticationSchemes =
        AuditAgentAuthenticationHandler.SchemeName)]
public sealed class AuditAgentHub : Hub
{
    private readonly AuditAgentConnectionRegistry _registry;
    private readonly ILogger<AuditAgentHub> _logger;

    public AuditAgentHub(
        AuditAgentConnectionRegistry registry,
        ILogger<AuditAgentHub> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<string> Register(string machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName) ||
            machineName.Length > 100 ||
            machineName.Any(char.IsControl))
        {
            throw new HubException(
                "A machine name of 1–100 characters is required.");
        }

        if (!_registry.TryRegister(
            Context.ConnectionId,
            machineName.Trim()))
        {
            throw new HubException(
                "Another Agent is already connected. " +
                "Close it before connecting this Agent.");
        }

        _logger.LogInformation(
            "Audit Agent connected: {MachineName}",
            machineName.Trim());

        await Clients.Caller.SendAsync(
            "DashboardHello",
            "Dashboard → Agent message received. " +
            "The connection is working.",
            Context.ConnectionAborted);

        return "Agent → Dashboard registration accepted.";
    }

    public DateTimeOffset Heartbeat()
    {
        if (!_registry.Heartbeat(Context.ConnectionId))
        {
            throw new HubException(
                "Register this Agent before sending a heartbeat.");
        }

        return DateTimeOffset.UtcNow;
    }

    // Only the registered Agent connection can acknowledge its own command.
    public bool CompleteOpenUrl(Guid commandId, bool opened)
    {
        return _registry.CompleteOpenUrl(
            Context.ConnectionId,
            commandId,
            opened);
    }

    public override async Task OnDisconnectedAsync(
        Exception? exception)
    {
        _registry.Remove(Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }
}

