using System.Collections.Concurrent;
using CityWebsiteAuditDashboard.Contracts;
using CityWebsiteAuditDashboard.Hubs;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using Microsoft.AspNetCore.SignalR;

namespace CityWebsiteAuditDashboard.Services.AuditAgent;

public sealed class AuditAgentDispatcher
{
    private readonly IHubContext<AuditAgentHub> _hub;
    private readonly AuditAgentConnectionRegistry _registry;
    private readonly ILogger<AuditAgentDispatcher> _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private string? _connection;
    private AuditAgentState _state = new();
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();
    private sealed record Pending(string Connection, AuditCommand Command, TaskCompletionSource<AuditReply> Completion);

    public AuditAgentDispatcher(IHubContext<AuditAgentHub> hub, AuditAgentConnectionRegistry registry,
        ILogger<AuditAgentDispatcher> logger)
    { _hub = hub; _registry = registry; _logger = logger; }

    public void Attach(string connection)
    {
        lock (_gate)
        {
            if (_connection == connection) return;
            _connection = connection;
            _state = new();
        }
    }

    public bool IsOwner(string connection)
    { lock (_gate) return _connection == connection; }

    public string? Connection
    { get { lock (_gate) return _connection; } }

    public void Detach(string connection)
    {
        lock (_gate)
        {
            if (_connection != connection) return;
            _connection = null;
            _state = new();
        }
        foreach (var pair in _pending.Where(x => x.Value.Connection == connection))
            pair.Value.Completion.TrySetException(new InvalidOperationException(
                "The workstation Agent disconnected. Saved steps remain in history; start a new session after reconnecting."));
    }

    public void Update(string connection, AuditAgentState state)
    {
        lock (_gate)
        {
            if (_connection == connection && state.Sequence > _state.Sequence) _state = state;
        }
    }

    private bool Fresh() => _registry.GetStatus() is { } status &&
        DateTimeOffset.UtcNow - status.LastSeenAt < TimeSpan.FromSeconds(40);

    public AuthenticatedAuditSessionResult? ActiveSession
    { get { lock (_gate) return Fresh() ? _state.Session : null; } }

    public AuthenticatedAuditProgressResult? Progress(Guid sessionId)
    { lock (_gate) return Fresh() && _state.Session?.SessionId == sessionId ? _state.Progress : null; }

    public bool Complete(string connection, AuditReply reply)
    {
        return _pending.TryGetValue(reply.Id, out var pending) && pending.Connection == connection &&
            pending.Completion.TrySetResult(reply);
    }

    public bool RequestWorkflowStop(Guid sessionId)
    {
        var pending = _pending.Values.FirstOrDefault(x => x.Command.SessionId == sessionId &&
            x.Command.Operation == "Run");
        if (pending is null) return false;
        _ = SendCancellationAsync(pending.Connection, pending.Command.Id, true);
        return true;
    }

    private async Task SendCancellationAsync(string connection, Guid id, bool workflowOnly = false)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _hub.Clients.Client(connection).SendAsync("CancelAudit", id, workflowOnly, timeout.Token);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not deliver audit cancellation."); }
    }

    public async Task<T> CallAsync<T>(AuditCommand command, CancellationToken cancellationToken = default)
    {
        // Stop can request cancellation while a workflow owns the operation gate.
        if (command.Operation == "Stop" || command.Operation == "Interrupt")
            foreach (var p in _pending.Values)
                await SendCancellationAsync(p.Connection, p.Command.Id);

        if (!await _operation.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
            throw new InvalidOperationException("Another audit operation is still stopping. Try again after it finishes.");
        try
        {
            string connection;
            lock (_gate)
            {
                connection = _connection ?? throw new InvalidOperationException("Start the workstation Audit Agent first.");
                if (!Fresh()) throw new InvalidOperationException("The Audit Agent is not responding.");
                if (command.Operation == "Start" && _state.Session is not null)
                    throw new InvalidOperationException("Close the active audit session before starting another.");
            }
            var pending = new Pending(connection, command,
                new(TaskCreationOptions.RunContinuationsAsynchronously));
            if (!_pending.TryAdd(command.Id, pending)) throw new InvalidOperationException("Duplicate command ID.");
            try
            {
                await _hub.Clients.Client(connection).SendAsync("ExecuteAudit", command, cancellationToken);
                AuditReply reply = await pending.Completion.Task.WaitAsync(
                    command.Operation is "Run" or "Batch" ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(5),
                    cancellationToken);
                if (reply.Cancelled) throw new OperationCanceledException("The audit operation was cancelled.");
                if (!reply.Succeeded) throw new InvalidOperationException(reply.Error ?? "The Agent could not complete the operation.");
                return AuditAgentProtocol.Read<T>(reply.Json);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                await SendCancellationAsync(connection, command.Id);
                // Await cleanup briefly so a new command is not sent into an old operation.
                try { await pending.Completion.Task.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
                throw;
            }
            finally { _pending.TryRemove(command.Id, out _); }
        }
        finally { _operation.Release(); }
    }
}
