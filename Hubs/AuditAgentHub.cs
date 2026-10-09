using CityWebsiteAuditDashboard.Contracts;
using CityWebsiteAuditDashboard.Services.AuditAgent;
using CityWebsiteAuditDashboard.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CityWebsiteAuditDashboard.Hubs;

[Authorize(Policy = AuditOperator.Policy)]
public sealed class AuditAgentHub(AuditAgentConnectionRegistry registry,
    AuditAgentDispatcher dispatcher, AuditAgentStore store) : Hub
{
    private string Owner => AuditOperator.RequireId(Context.User);

    public async Task<string> RegisterV2(string machineName, int protocolVersion)
    {
        if (protocolVersion != AuditAgentProtocol.Version)
            throw new HubException("Install matching dashboard and Agent versions.");
        if (string.IsNullOrWhiteSpace(machineName) || machineName.Length > 100 || machineName.Any(char.IsControl))
            throw new HubException("A valid workstation name is required.");
        string owner = Owner;
        if (!registry.TryRegister(Context.ConnectionId, owner, machineName.Trim()))
            throw new HubException("Your Windows account already has an Agent connected on another window or laptop. Stop that Agent first.");
        try
        {
            await store.RegisterAsync(Context.ConnectionId);
            dispatcher.Attach(owner, Context.ConnectionId);
        }
        catch
        {
            registry.Remove(Context.ConnectionId);
            dispatcher.Detach(Context.ConnectionId);
            await store.DisconnectAsync(Context.ConnectionId);
            throw;
        }
        return "Agent connected for " + Context.User?.Identity?.Name + ". Audit protocol v" + AuditAgentProtocol.Version + ".";
    }

    public DateTimeOffset ReportState(AuditAgentState state)
    {
        RequireOwner();
        if (!registry.Heartbeat(Context.ConnectionId)) throw new HubException("Register the Agent first.");
        dispatcher.Update(Owner, Context.ConnectionId, state);
        return DateTimeOffset.UtcNow;
    }

    public Task<int> SaveAudit(AuditWrite write)
    { RequireOwner(); return store.SaveAsync(Context.ConnectionId, write); }

    public bool CompleteAudit(AuditReply reply)
    { RequireOwner(); return dispatcher.Complete(Owner, Context.ConnectionId, reply); }

    public bool CompleteOpenUrl(Guid commandId, bool opened)
    { RequireOwner(); return registry.CompleteOpenUrl(Context.ConnectionId, commandId, opened); }

    private void RequireOwner()
    {
        if (!dispatcher.IsOwner(Owner, Context.ConnectionId))
            throw new HubException("This Agent is not registered for your account.");
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        dispatcher.Detach(Context.ConnectionId);
        registry.Remove(Context.ConnectionId);
        await store.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

