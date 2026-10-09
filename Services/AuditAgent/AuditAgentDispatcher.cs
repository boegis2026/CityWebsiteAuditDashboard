using System.Collections.Concurrent;
using CityWebsiteAuditDashboard.Contracts;
using CityWebsiteAuditDashboard.Hubs;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using Microsoft.AspNetCore.SignalR;

namespace CityWebsiteAuditDashboard.Services.AuditAgent;

public sealed class AuditAgentDispatcher
{
    private sealed class Lane
    {
        public readonly SemaphoreSlim Operation = new(1, 1);
        public string? Connection;
        public AuditAgentState State = new();
    }
    private sealed record Pending(string Owner, string Connection, AuditCommand Command, TaskCompletionSource<AuditReply> Completion);
    private readonly object _gate = new();
    private readonly Dictionary<string, Lane> _lanes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();
    private readonly IHubContext<AuditAgentHub> _hub;
    private readonly AuditAgentConnectionRegistry _registry;
    private readonly ILogger<AuditAgentDispatcher> _logger;

    public AuditAgentDispatcher(IHubContext<AuditAgentHub> hub, AuditAgentConnectionRegistry registry,
        ILogger<AuditAgentDispatcher> logger)
    { _hub = hub; _registry = registry; _logger = logger; }

    public void Attach(string owner, string connection)
    {
        lock (_gate)
        {
            if (!_registry.IsOwner(connection, owner)) throw new InvalidOperationException("Register the Agent first.");
            if (!_lanes.TryGetValue(owner, out var lane)) _lanes[owner] = lane = new();
            if (lane.Connection == connection) return;
            lane.Connection = connection;
            lane.State = new();
        }
    }

    public bool IsOwner(string owner, string connection)
    { lock (_gate) return _lanes.TryGetValue(owner, out var lane) && lane.Connection == connection && _registry.IsOwner(connection, owner); }

    public void Detach(string connection)
    {
        lock (_gate)
        {
            foreach (var lane in _lanes.Values.Where(x => x.Connection == connection))
            { lane.Connection = null; lane.State = new(); }
        }
        foreach (var pending in _pending.Values.Where(x => x.Connection == connection))
            pending.Completion.TrySetException(new InvalidOperationException(
                "Your Agent disconnected. Saved results remain in history; start a new session after reconnecting."));
    }

    public void Update(string owner, string connection, AuditAgentState state)
    {
        lock (_gate)
            if (IsOwner(owner, connection) && state.Sequence > _lanes[owner].State.Sequence)
                _lanes[owner].State = state;
    }

    public AuthenticatedAuditSessionResult? ActiveSession(string owner)
    {
        lock (_gate) return _lanes.TryGetValue(owner, out var lane) && lane.Connection is { } connection &&
            _registry.IsFresh(connection) ? lane.State.Session : null;
    }

    public AuthenticatedAuditProgressResult? Progress(string owner, Guid sessionId)
    {
        lock (_gate) return ActiveSession(owner)?.SessionId == sessionId ? _lanes[owner].State.Progress : null;
    }

    public bool Complete(string owner, string connection, AuditReply reply) => IsOwner(owner, connection) &&
        _pending.TryGetValue(reply.Id, out var pending) && pending.Owner == owner && pending.Connection == connection &&
        pending.Completion.TrySetResult(reply);

    public bool RequestWorkflowStop(string owner, Guid sessionId)
    {
        lock (_gate)
        {
            if (ActiveSession(owner)?.SessionId != sessionId) return false;
            var pending = _pending.Values.FirstOrDefault(x => x.Owner == owner && x.Command.SessionId == sessionId &&
                x.Command.Operation == "Run" && x.Connection == _lanes[owner].Connection);
            if (pending is null) return false;
            _ = SendCancellationAsync(pending.Connection, pending.Command.Id, true);
            return true;
        }
    }

    private async Task SendCancellationAsync(string connection, Guid id, bool workflowOnly = false)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _hub.Clients.Client(connection).SendAsync("CancelAudit", id, workflowOnly, timeout.Token);