using CityWebsiteAuditDashboard.Services.Security;
using CityWebsiteAuditDashboard.Contracts;
using CityWebsiteAuditDashboard.Services.AuditAgent;

namespace CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

// Controllers keep their existing interface. Every browser operation now runs on the Agent.
public sealed class AuthenticatedAuditService : IAuthenticatedAuditService
{
    private readonly AuditAgentDispatcher _agent;
    private readonly AuditOperator _operator;
    public AuthenticatedAuditService(AuditAgentDispatcher agent, AuditOperator auditOperator)
    { _agent = agent; _operator = auditOperator; }

    public AuthenticatedAuditSessionResult? GetActiveSession() => _agent.ActiveSession(_operator.RequireId());
    public AuthenticatedAuditProgressResult? GetProgress(Guid sessionId) => _agent.Progress(_operator.RequireId(), sessionId);
    public bool RequestAutomaticWorkflowStop(Guid sessionId) => _agent.RequestWorkflowStop(_operator.RequireId(), sessionId);

    public Task<AuthenticatedAuditSessionResult> StartSessionAsync(AuthenticatedAuditStartRequest request,
        CancellationToken cancellationToken = default) =>
        _agent.CallAsync<AuthenticatedAuditSessionResult>(_operator.RequireId(), new() { Operation = "Start", Start = request }, cancellationToken);

    public Task<AuthenticatedAuditStepResult> ScanCurrentStepAsync(Guid sessionId,
        CancellationToken cancellationToken = default) =>
        Send<AuthenticatedAuditStepResult>("Scan", sessionId, cancellationToken);

    public Task<AuthenticatedAuditBatchResult> ScanBatchAsync(Guid sessionId, IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default) =>
        _agent.CallAsync<AuthenticatedAuditBatchResult>(_operator.RequireId(), new()
        { Operation = "Batch", SessionId = sessionId, Urls = urls.ToList() }, cancellationToken);

    public Task<AuthenticatedAuditNavigationAnalysisResult> AnalyzeCurrentStateAsync(Guid sessionId,
        CancellationToken cancellationToken = default) =>
        Send<AuthenticatedAuditNavigationAnalysisResult>("Analyze", sessionId, cancellationToken);

    public Task<AuthenticatedAuditFieldFillResult> FillCurrentStateAsync(Guid sessionId,
        CancellationToken cancellationToken = default) =>
        Send<AuthenticatedAuditFieldFillResult>("Fill", sessionId, cancellationToken);

    public Task<AuthenticatedAuditAutomaticNavigationResult> PreviewAutomaticStepAsync(Guid sessionId,
        CancellationToken cancellationToken = default) =>
        Send<AuthenticatedAuditAutomaticNavigationResult>("Preview", sessionId, cancellationToken);

    public Task<AuthenticatedAuditAutomaticNavigationResult> AdvanceAutomaticStepAsync(Guid sessionId,
        CancellationToken cancellationToken = default) =>
        Send<AuthenticatedAuditAutomaticNavigationResult>("Advance", sessionId, cancellationToken);

    public Task<AuthenticatedAuditAutomaticCycleResult> ScanAndAdvanceAutomaticStepAsync(Guid sessionId,
        CancellationToken cancellationToken = default) =>
        Send<AuthenticatedAuditAutomaticCycleResult>("Cycle", sessionId, cancellationToken);

    public Task<AuthenticatedAuditAutomaticRunResult> RunAutomaticWorkflowAsync(Guid sessionId,
        int maximumStateCount = 25, CancellationToken cancellationToken = default) =>
        _agent.CallAsync<AuthenticatedAuditAutomaticRunResult>(_operator.RequireId(), new()
        { Operation = "Run", SessionId = sessionId, MaximumStates = Math.Clamp(maximumStateCount, 1, 25) }, cancellationToken);

    public async Task StopSessionAsync(Guid sessionId, bool markLastStepAsFinal,
        CancellationToken cancellationToken = default) =>
        await _agent.CallAsync<bool>(_operator.RequireId(), new()
        { Operation = "Stop", SessionId = sessionId, MarkFinal = markLastStepAsFinal }, cancellationToken);

    public async Task InterruptAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (_agent.ActiveSession(_operator.RequireId()) is null) return;
        await _agent.CallAsync<bool>(_operator.RequireId(), new() { Operation = "Interrupt" }, cancellationToken);
    }

    private Task<T> Send<T>(string operation, Guid sessionId, CancellationToken token) =>
        _agent.CallAsync<T>(_operator.RequireId(), new() { Operation = operation, SessionId = sessionId }, token);
}

