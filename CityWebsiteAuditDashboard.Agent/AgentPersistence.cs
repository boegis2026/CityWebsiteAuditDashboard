using CityWebsiteAuditDashboard.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace CityWebsiteAuditDashboard.Agent;

// No connection string, DbContext, or SQL client belongs in the workstation Agent.
public sealed class AgentPersistence
{
    private readonly HubConnection _connection;
    private readonly CancellationToken _lifetime;
    public bool Faulted { get; private set; }

    public AgentPersistence(HubConnection connection, CancellationToken lifetime)
    {
        _connection = connection;
        _lifetime = lifetime;
    }

    public async Task<int> SendAsync(AuditWrite request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Once a write is sent, finish its acknowledgement even if the HTTP caller cancels.
        // Otherwise SQL could commit while the engine reuses the old next-step number.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        if (System.Text.Encoding.UTF8.GetByteCount(AuditAgentProtocol.Serialize(request)) >
            AuditAgentProtocol.MaximumMessageBytes - 4096)
        {
            Faulted = true;
            throw new InvalidOperationException("This scan exceeds the 16 MB transport limit. It was not saved; use a smaller page or workflow state.");
        }
        Exception? last = null;
        // Reuse the same write ID: an acknowledgement lost after COMMIT must not duplicate data.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            linked.Token.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(40));
                return await _connection.InvokeAsync<int>("SaveAudit", request, timeout.Token);
            }
            catch (Exception ex) when (!linked.IsCancellationRequested)
            {
                last = ex;
                if (_connection.State != HubConnectionState.Connected) break;
                if (attempt < 2) await Task.Delay(1000, linked.Token);
            }
        }
        Faulted = true;
        throw new InvalidOperationException(
            "SQL acknowledgement failed. This browser session will be interrupted; inspect saved history before starting again.", last);
    }
}

