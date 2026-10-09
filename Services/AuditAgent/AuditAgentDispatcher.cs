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

    private sealed record Pending(
        string Owner,
        string Connection,
        AuditCommand Command,
        TaskCompletionSource<AuditReply> Completion);

    private readonly object _gate = new();

    private readonly Dictionary<string, Lane> _lanes =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();

    private readonly IHubContext<AuditAgentHub> _hub;
    private readonly AuditAgentConnectionRegistry _registry;
    private readonly ILogger<AuditAgentDispatcher> _logger;

    public AuditAgentDispatcher(
        IHubContext<AuditAgentHub> hub,
        AuditAgentConnectionRegistry registry,
        ILogger<AuditAgentDispatcher> logger)
    {
        _hub = hub;
        _registry = registry;
        _logger = logger;
    }

    public void Attach(string owner, string connection)
    {
        lock (_gate)
        {
            if (!_registry.IsOwner(connection, owner))
                throw new InvalidOperationException(
                    "Register the Agent first.");

            if (!_lanes.TryGetValue(owner, out var lane))
                _lanes[owner] = lane = new();

            if (lane.Connection == connection)
                return;

            lane.Connection = connection;
            lane.State = new();
        }
    }

    public bool IsOwner(string owner, string connection)
    {
        lock (_gate)
        {
            return _lanes.TryGetValue(owner, out var lane) &&
                lane.Connection == connection &&
                _registry.IsOwner(connection, owner);
        }
    }

    public void Detach(string connection)
    {
        lock (_gate)
        {
            foreach (var lane in _lanes.Values.Where(
                x => x.Connection == connection))
            {
                lane.Connection = null;
                lane.State = new();
            }
        }

        foreach (var pending in _pending.Values.Where(
            x => x.Connection == connection))
        {
            pending.Completion.TrySetException(
                new InvalidOperationException(
                    "Your Agent disconnected. Saved results remain in history; " +
                    "start a new session after reconnecting."));
        }
    }

    public void Update(
        string owner,
        string connection,
        AuditAgentState state)
    {
        lock (_gate)
        {
            if (IsOwner(owner, connection) &&
                state.Sequence > _lanes[owner].State.Sequence)
            {
                _lanes[owner].State = state;
            }
        }
    }

    public AuthenticatedAuditSessionResult? ActiveSession(string owner)
    {
        lock (_gate)
        {
            return _lanes.TryGetValue(owner, out var lane) &&
                lane.Connection is { } connection &&
                _registry.IsFresh(connection)
                    ? lane.State.Session
                    : null;
        }
    }

    public AuthenticatedAuditProgressResult? Progress(
        string owner,
        Guid sessionId)
    {
        lock (_gate)
        {
            return ActiveSession(owner)?.SessionId == sessionId
                ? _lanes[owner].State.Progress
                : null;
        }
    }

    public bool Complete(
        string owner,
        string connection,
        AuditReply reply)
    {
        return IsOwner(owner, connection) &&
            _pending.TryGetValue(reply.Id, out var pending) &&
            pending.Owner == owner &&
            pending.Connection == connection &&
            pending.Completion.TrySetResult(reply);
    }

    public bool RequestWorkflowStop(string owner, Guid sessionId)
    {
        lock (_gate)
        {
            if (ActiveSession(owner)?.SessionId != sessionId)
                return false;

            var pending = _pending.Values.FirstOrDefault(x =>
                x.Owner == owner &&
                x.Command.SessionId == sessionId &&
                x.Command.Operation == "Run" &&
                x.Connection == _lanes[owner].Connection);

            if (pending is null)
                return false;

            _ = SendCancellationAsync(
                pending.Connection,
                pending.Command.Id,
                true);

            return true;
        }
    }

    private async Task SendCancellationAsync(
        string connection,
        Guid id,
        bool workflowOnly = false)
    {
        try
        {
            using var timeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await _hub.Clients.Client(connection).SendAsync(
                "CancelAudit",
                id,
                workflowOnly,
                timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not deliver audit cancellation.");
        }
    }

    private void ValidateSession(Lane lane, AuditCommand command)
    {
        if (command.Operation == "Start")
        {
            if (lane.State.Session is not null)
            {
                throw new InvalidOperationException(
                    "Close your active session before starting another.");
            }
        }
        else if (command.Operation != "Interrupt" &&
            (command.SessionId == Guid.Empty ||
             lane.State.Session?.SessionId != command.SessionId))
        {
            throw new InvalidOperationException(
                "This audit session does not belong to your active Agent.");
        }
    }

    public async Task<T> CallAsync<T>(
        string owner,
        AuditCommand command,
        CancellationToken cancellationToken = default)
    {
        Lane lane;
        string connection;

        lock (_gate)
        {
            if (!_lanes.TryGetValue(owner, out lane!) ||
                lane.Connection is null)
            {
                throw new InvalidOperationException(
                    "Your Audit Agent is not connected. " +
                    "Open the Agent connection page for instructions.");
            }

            connection = lane.Connection;

            if (!_registry.IsFresh(connection))
            {
                throw new InvalidOperationException(
                    "Your Agent is not responding.");
            }

            ValidateSession(lane, command);
        }

        // Validate ownership before cancelling this user's operation.
        if (command.Operation is "Stop" or "Interrupt")
        {
            foreach (var pending in _pending.Values.Where(x =>
                x.Owner == owner &&
                x.Connection == connection))
            {
                await SendCancellationAsync(
                    connection,
                    pending.Command.Id);
            }
        }

        if (!await lane.Operation.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken))
        {
            throw new InvalidOperationException(
                "Your previous audit operation is still stopping. " +
                "Try again shortly.");
        }

        try
        {
            lock (_gate)
            {
                // A queued command must never move to a replacement Agent.
                if (lane.Connection != connection ||
                    !IsOwner(owner, connection) ||
                    !_registry.IsFresh(connection))
                {
                    throw new InvalidOperationException(
                        "Your Agent connection changed. " +
                        "Refresh before trying again.");
                }

                ValidateSession(lane, command);
            }

            var pending = new Pending(
                owner,
                connection,
                command,
                new(TaskCreationOptions.RunContinuationsAsynchronously));

            if (!_pending.TryAdd(command.Id, pending))
            {
                throw new InvalidOperationException(
                    "Duplicate command ID.");
            }

            try
            {
                await _hub.Clients.Client(connection).SendAsync(
                    "ExecuteAudit",
                    command,
                    cancellationToken);

                var reply = await pending.Completion.Task.WaitAsync(
                    command.Operation is "Run" or "Batch"
                        ? TimeSpan.FromHours(2)
                        : TimeSpan.FromMinutes(5),
                    cancellationToken);

                if (reply.Cancelled)
                {
                    throw new OperationCanceledException(
                        "The audit operation was cancelled.");
                }

                if (!reply.Succeeded)
                {
                    throw new InvalidOperationException(
                        reply.Error ??
                        "The Agent could not complete the operation.");
                }

                return AuditAgentProtocol.Read<T>(reply.Json);
            }
            catch (Exception ex) when (
                ex is OperationCanceledException or TimeoutException)
            {
                await SendCancellationAsync(connection, command.Id);

                try
                {
                    await pending.Completion.Task.WaitAsync(
                        TimeSpan.FromSeconds(10));
                }
                catch
                {
                    // Preserve the original cancellation or timeout.
                }

                throw;
            }
            finally
            {
                _pending.TryRemove(command.Id, out _);
            }
        }
        finally
        {
            lane.Operation.Release();
        }
    }

    // Server shutdown only; not exposed to a controller or hub method.
    public async Task InterruptAllAsync(CancellationToken token)
    {
        string[] owners;

        lock (_gate)
        {
            owners = _lanes
                .Where(x => x.Value.Connection is not null)
                .Select(x => x.Key)
                .ToArray();
        }

        await Task.WhenAll(owners.Select(async owner =>
        {
            try
            {
                await CallAsync<bool>(
                    owner,
                    new() { Operation = "Interrupt" },
                    token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not interrupt an Agent during shutdown.");
            }
        }));
    }
}
