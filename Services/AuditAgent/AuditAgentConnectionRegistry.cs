namespace CityWebsiteAuditDashboard.Services.AuditAgent;

public sealed record AuditAgentConnectionStatus(
    string MachineName,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastSeenAt);

// One IIS worker process and one connected Agent for this proof of concept.
public sealed class AuditAgentConnectionRegistry
{
    private readonly object _gate = new();

    private string? _connectionId;
    private AuditAgentConnectionStatus? _status;

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
}
