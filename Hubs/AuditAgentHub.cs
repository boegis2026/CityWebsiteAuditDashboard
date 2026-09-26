using CityWebsiteAuditDashboard.Contracts;
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
    private readonly AuditAgentDispatcher _dispatcher;
    private readonly AuditAgentStore _store;

    public AuditAgentHub(
        AuditAgentConnectionRegistry registry,
        AuditAgentDispatcher dispatcher,
        AuditAgentStore store)
    {
        _registry = registry;
        _dispatcher = dispatcher;
        _store = store;
    }

    public async Task<string> RegisterV2(
        string machineName,
        int protocolVersion)
    {
        if (protocolVersion != AuditAgentProtocol.Version)
        {
            throw new HubException(
                "Publish matching dashboard and Agent versions.");
        }

        if (string.IsNullOrWhiteSpace(machineName) ||
            machineName.Length > 100 ||
            machineName.Any(char.IsControl))
        {
            throw new HubException(
                "A valid workstation name is required.");
        }

        if (!_registry.TryRegister(
            Context.ConnectionId,
            machineName.Trim()))
        {
            throw new HubException(
                "Another Agent is already connected.");
        }

        await _store.RegisterAsync(Context.ConnectionId);

        _dispatcher.Attach(Context.ConnectionId);

        return "Agent → Dashboard registration accepted. " +
               "Audit protocol v2.";
    }

    public DateTimeOffset ReportState(AuditAgentState state)
    {
        RequireOwner();

        if (!_registry.Heartbeat(Context.ConnectionId))
        {
            throw new HubException(
                "Register the Agent first.");
        }

        _dispatcher.Update(Context.ConnectionId, state);

        return DateTimeOffset.UtcNow;
    }

    public Task<int> SaveAudit(AuditWrite write)
    {
        RequireOwner();

        return _store.SaveAsync(
            Context.ConnectionId,
            write);
    }

    public bool CompleteAudit(AuditReply reply)
    {
        RequireOwner();

        return _dispatcher.Complete(
            Context.ConnectionId,
            reply);
    }

    public bool CompleteOpenUrl(
        Guid commandId,
        bool opened)
    {
        RequireOwner();

        return _registry.CompleteOpenUrl(
            Context.ConnectionId,
            commandId,
            opened);
    }

    private void RequireOwner()
    {
        if (!_dispatcher.IsOwner(Context.ConnectionId))
        {
            throw new HubException(
                "This Agent is not registered.");
        }
    }

    public override async Task OnDisconnectedAsync(
        Exception? exception)
    {
        _dispatcher.Detach(Context.ConnectionId);

        _registry.Remove(Context.ConnectionId);

        await _store.DisconnectAsync(Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }
}
