using System.Text;
using CityWebsiteAuditDashboard.Contracts;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace CityWebsiteAuditDashboard.Agent;

public sealed class AgentAuditWorker : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly AgentAuditEngine _engine;
    private readonly AgentPersistence _persistence;
    private readonly AgentBrowser _diagnosticBrowser;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, AuditReply> _replies = new();
    private CancellationTokenSource? _currentCancellation;
    private AuditCommand? _currentCommand;
    private Task _work = Task.CompletedTask;
    private bool _busy;
    private bool _disposed;
    private long _sequence;

    public AgentAuditWorker(HubConnection connection, AgentAuditEngine engine, AgentPersistence persistence,
        AgentBrowser diagnosticBrowser, ILogger logger, CancellationToken lifetime)
    {
        _connection = connection; _engine = engine; _persistence = persistence;
        _diagnosticBrowser = diagnosticBrowser; _logger = logger;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    }

    public AuditAgentState CaptureState()
    {
        // Capture and number snapshots under one lock so an older heartbeat cannot overwrite a new session.
        lock (_gate)
        {
            var session = _engine.GetActiveSession();
            return new AuditAgentState
            {
                Sequence = ++_sequence,
                Session = session,
                Progress = session is null ? null : _engine.GetProgress(session.SessionId)
            };
        }
    }

    public void Execute(AuditCommand command)
    {
        lock (_gate)
        {
            if (_disposed || _lifetime.IsCancellationRequested) return;
            if (_replies.TryGetValue(command.Id, out var saved))
            { _ = SendReplyAsync(saved); return; }
            if (_currentCommand?.Id == command.Id) return;
            if (_busy || _replies.Count >= 4096)
            {
                _ = SendReplyAsync(new AuditReply
                {
                    Id = command.Id,
                    Error = "The Agent is busy or its command limit was reached. Finish the current operation or restart the Agent."
                });
                return;
            }
            _busy = true;
            _currentCommand = command;
            _currentCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _work = Task.Run(() => ExecuteCoreAsync(command, _currentCancellation.Token));
        }
    }

    public void Cancel(Guid commandId, bool workflowOnly)
    {
        lock (_gate)
        {
            if (_currentCommand?.Id != commandId) return;
            if (workflowOnly && _currentCommand.Operation == "Run")
            {
                if (!_engine.RequestAutomaticWorkflowStop(_currentCommand.SessionId))
                    _currentCancellation?.Cancel();
            }
            else
                _currentCancellation?.Cancel();
        }
    }

    private async Task ExecuteCoreAsync(AuditCommand command, CancellationToken token)
    {
        var reply = new AuditReply { Id = command.Id };
        try
        {
            if (command.Operation == "Start") await _diagnosticBrowser.CloseAsync();
            object result = command.Operation switch
            {
                "Start" => await _engine.StartSessionAsync(command.Start ?? throw new ArgumentException("Missing start request."), token),
                "Scan" => await _engine.ScanCurrentStepAsync(command.SessionId, token),
                "Batch" => await _engine.ScanBatchAsync(command.SessionId, command.Urls, token),
                "Analyze" => await _engine.AnalyzeCurrentStateAsync(command.SessionId, token),
                "Fill" => await _engine.FillCurrentStateAsync(command.SessionId, token),
                "Preview" => await _engine.PreviewAutomaticStepAsync(command.SessionId, token),
                "Advance" => await _engine.AdvanceAutomaticStepAsync(command.SessionId, token),
                "Cycle" => await _engine.ScanAndAdvanceAutomaticStepAsync(command.SessionId, token),
                "Run" => await _engine.RunAutomaticWorkflowAsync(command.SessionId, command.MaximumStates, token),
                "Stop" => await StopAsync(command, token),
                "Interrupt" => await InterruptAsync(token),
                _ => throw new ArgumentException("Unknown audit command.")
            };
            reply.Json = AuditAgentProtocol.Serialize(result);
            // Account for escaping the JSON payload again inside the SignalR envelope.
            if (Encoding.UTF8.GetByteCount(AuditAgentProtocol.Serialize(reply)) > AuditAgentProtocol.MaximumMessageBytes - 4096)
                throw new InvalidOperationException("The result is too large for transport. Saved steps remain available in audit history.");
            reply.Succeeded = true;
        }
        catch (OperationCanceledException)
        { reply.Cancelled = true; reply.Error = "The operation was cancelled."; reply.Json = "null"; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent audit command {Operation} failed.", command.Operation);
            reply.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            reply.Json = "null";
        }
        finally
        {
            if (_persistence.Faulted)
            {
                // Never continue scanning after an uncertain save; the next step number must remain trustworthy.
                try { await _engine.InterruptAllSessionsAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Audit interruption after save failure."); }
                _lifetime.Cancel();
            }
            _engine.ResetProgressAfterCommand(command.SessionId,
                reply.Cancelled ? "Cancelled" : reply.Succeeded ? "Idle" : "Failed");
            lock (_gate)
            {
                _replies[command.Id] = reply;
                _currentCancellation?.Dispose();
                _currentCancellation = null;
                _currentCommand = null;
                _busy = false;
            }
        }
        // State is pushed before completion so dashboard refresh sees the correct session immediately.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _connection.InvokeAsync<DateTimeOffset>("ReportState", CaptureState(), timeout.Token);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not publish final Agent state."); }
        await SendReplyAsync(reply);
    }

    private async Task SendReplyAsync(AuditReply reply)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            bool accepted = await _connection.InvokeAsync<bool>("CompleteAudit", reply, timeout.Token);
            if (!accepted) _logger.LogWarning("Dashboard no longer expects command {CommandId}. Inspect history before retrying.", reply.Id);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not acknowledge audit command."); }
    }

    private async Task<bool> StopAsync(AuditCommand command, CancellationToken token)
    { await _engine.StopSessionAsync(command.SessionId, command.MarkFinal, token); return true; }
    private async Task<bool> InterruptAsync(CancellationToken token)
    { await _engine.InterruptAllSessionsAsync(token); return true; }

    public void OpenDiagnostic(Guid commandId, string url)
    {
        lock (_gate)
        {
            if (_disposed || _lifetime.IsCancellationRequested) return;
            if (_busy || _engine.GetActiveSession() is not null)
            { _ = AcknowledgeOpenAsync(commandId, false); return; }
            _busy = true;
            _work = Task.Run(async () =>
            {
                bool opened = false;
                try { await _diagnosticBrowser.OpenAsync(url, _lifetime.Token); opened = true; }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not open diagnostic Edge window."); }
                try { await AcknowledgeOpenAsync(commandId, opened); }
                finally { lock (_gate) _busy = false; }
            });
        }
    }

    private async Task AcknowledgeOpenAsync(Guid id, bool opened)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (!await _connection.InvokeAsync<bool>("CompleteOpenUrl", id, opened, timeout.Token) && opened)
                await _diagnosticBrowser.CloseAsync();
        }
        catch (Exception ex)
        {
            if (opened) await _diagnosticBrowser.CloseAsync();
            _logger.LogWarning(ex, "Could not acknowledge diagnostic browser.");
        }
    }

    public bool MustReconnect => _lifetime.IsCancellationRequested;

    public async ValueTask DisposeAsync()
    {
        Task work;
        lock (_gate)
        {
            _disposed = true;
            _lifetime.Cancel();
            _currentCancellation?.Cancel();
            work = _work;
        }
        // Browser calls may have their own timeouts; closing them unblocks pending Playwright work.
        await _engine.CloseBrowsersForDisconnectAsync();
        try { await work.WaitAsync(TimeSpan.FromSeconds(150)); }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "The Agent could not finish stopping. Exit and restart this Agent before another session.");
            throw;
        }
        await _engine.InterruptAllSessionsAsync();
        await _diagnosticBrowser.CloseAsync();
        _lifetime.Dispose();
    }
}

