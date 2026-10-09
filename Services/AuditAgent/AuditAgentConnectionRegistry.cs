namespace CityWebsiteAuditDashboard.Services.AuditAgent;

public sealed record AuditAgentConnectionStatus(string MachineName, DateTimeOffset ConnectedAt, DateTimeOffset LastSeenAt);
public sealed record AuditAgentOpenUrlCommand(string ConnectionId, Guid CommandId, Task<bool> Completion);

// One active Agent per authenticated operator, within one IIS worker process.
public sealed class AuditAgentConnectionRegistry
{
    private sealed class Entry(string owner, string connection, string machine)
    {
        public string Owner { get; } = owner;
        public string Connection { get; } = connection;
        public AuditAgentConnectionStatus Status { get; set; } = new(machine, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public Guid? PendingId;
        public TaskCompletionSource<bool>? PendingCompletion;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _connections = new(StringComparer.Ordinal);

    public bool TryRegister(string connection, string owner, string machine)
    {
        lock (_gate)
        {
            if (_connections.TryGetValue(connection, out var existing)) return existing.Owner == owner;
            if (_owners.ContainsKey(owner)) return false;
            var entry = new Entry(owner, connection, machine);
            _owners.Add(owner, entry);
            _connections.Add(connection, entry);
            return true;
        }
    }

    public bool IsOwner(string connection, string owner)
    { lock (_gate) return _connections.TryGetValue(connection, out var entry) && entry.Owner == owner; }

    public string? OwnerFor(string connection)
    { lock (_gate) return _connections.TryGetValue(connection, out var entry) ? entry.Owner : null; }

    public bool Heartbeat(string connection)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connection, out var entry)) return false;
            entry.Status = entry.Status with { LastSeenAt = DateTimeOffset.UtcNow };
            return true;
        }
    }

    public AuditAgentConnectionStatus? GetStatus(string owner)
    { lock (_gate) return _owners.TryGetValue(owner, out var entry) ? entry.Status : null; }

    public bool IsFresh(string connection)
    {
        lock (_gate) return _connections.TryGetValue(connection, out var entry) &&
            DateTimeOffset.UtcNow - entry.Status.LastSeenAt < TimeSpan.FromSeconds(40);
    }

    public void Remove(string connection)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connection, out var entry)) return;
            _owners.Remove(entry.Owner);
            entry.PendingCompletion?.TrySetResult(false);
        }
    }

    public string[] TakeExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            var expired = _connections.Values.Where(x => now - x.Status.LastSeenAt > TimeSpan.FromSeconds(45))
                .Select(x => x.Connection).ToArray();
            foreach (var connection in expired) Remove(connection);
            return expired;
        }
    }

    public AuditAgentOpenUrlCommand? BeginOpenUrl(string owner)
    {
        lock (_gate)
        {
            if (!_owners.TryGetValue(owner, out var entry) || !IsFresh(entry.Connection) || entry.PendingId.HasValue)
                return null;
            entry.PendingId = Guid.NewGuid();
            entry.PendingCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new(entry.Connection, entry.PendingId.Value, entry.PendingCompletion.Task);
        }
    }

    public bool CompleteOpenUrl(string connection, Guid commandId, bool opened)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connection, out var entry) || entry.PendingId != commandId || entry.PendingCompletion is null)
                return false;
            entry.PendingCompletion.TrySetResult(opened);
            entry.PendingId = null;
            entry.PendingCompletion = null;
            return true;
        }
    }

    public void CancelOpenUrl(string connection, Guid commandId) => CompleteOpenUrl(connection, commandId, false);
}
