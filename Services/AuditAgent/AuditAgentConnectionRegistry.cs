namespace CityWebsiteAuditDashboard.Services.AuditAgent;

public sealed record AuditAgentConnectionStatus(
    string MachineName,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastSeenAt);

public sealed record AuditAgentOpenUrlCommand(
    string ConnectionId,
    Guid CommandId,
    Task<bool> Completion);

// One IIS worker process and one connected Agent for this proof of concept.
public sealed class AuditAgentConnectionRegistry
{
    private readonly object _gate = new();

    private string? _connectionId;
    private AuditAgentConnectionStatus? _status;
    private Guid? _pendingCommandId;
    private TaskCompletionSource<bool>? _pendingCompletion;

    public bool TryRegister(
        string connectionId,
        string machineName)
    {
        lock (_gate)
        {
            if (_connectionId is not null &&
                _connectionId != connectionId)
            {
                return false;
            }

            _connectionId = connectionId;

            var now = DateTimeOffset.UtcNow;

            _status = new AuditAgentConnectionStatus(
                machineName,
                _status?.ConnectedAt ?? now,
                now);

            return true;
        }
    }

    public bool Heartbeat(string connectionId)
    {
        lock (_gate)
        {
            if (_connectionId != connectionId ||
                _status is null)
            {
                return false;
            }

            _status = _status with
            {
                LastSeenAt = DateTimeOffset.UtcNow
            };

            return true;
        }
    }

    public void Remove(string connectionId)
    {
        lock (_gate)
        {
            // A delayed disconnect must never clear a newer connection.
            if (_connectionId == connectionId)
            {
                _connectionId = null;
                _status = null;
                _pendingCommandId = null;
                _pendingCompletion?.TrySetResult(false);
                _pendingCompletion = null;
            }
        }
    }

    public AuditAgentConnectionStatus? GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    // Reserve one command at a time. A stale or disconnected Agent cannot
    // receive browser launch requests.
    public AuditAgentOpenUrlCommand? BeginOpenUrl()
    {
        lock (_gate)
        {
            if (_connectionId is null ||
                _status is null ||
                DateTimeOffset.UtcNow - _status.LastSeenAt >=
                    TimeSpan.FromSeconds(45) ||
                _pendingCommandId is not null)
            {
                return null;
            }

            Guid commandId = Guid.NewGuid();
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _pendingCommandId = commandId;
            _pendingCompletion = completion;

            return new AuditAgentOpenUrlCommand(
                _connectionId,
                commandId,
                completion.Task);
        }
    }

    public bool CompleteOpenUrl(
        string connectionId,
        Guid commandId,
        bool opened)
    {
        lock (_gate)
        {
            if (_connectionId != connectionId ||
                _pendingCommandId != commandId ||
                _pendingCompletion is null)
            {
                return false;
            }

            _pendingCompletion.TrySetResult(opened);
            _pendingCompletion = null;
            _pendingCommandId = null;
            return true;
        }
    }

    public void CancelOpenUrl(Guid commandId)
    {
        lock (_gate)
        {
            if (_pendingCommandId != commandId)
            {
                return;
            }

            _pendingCompletion?.TrySetResult(false);
            _pendingCompletion = null;
            _pendingCommandId = null;
        }
    }
}
