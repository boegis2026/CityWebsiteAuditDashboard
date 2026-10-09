using System.Text.Json;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

namespace CityWebsiteAuditDashboard.Contracts;

public static class AuditAgentProtocol
{
    public const int Version = 3;
    public const int MaximumMessageBytes = 16 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)
        ?? throw new InvalidOperationException("The Agent returned an empty result.");
}

public sealed class AuditCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Operation { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public AuthenticatedAuditStartRequest? Start { get; set; }
    public List<string> Urls { get; set; } = new();
    public int MaximumStates { get; set; } = 25;
    public bool MarkFinal { get; set; }
}

public sealed class AuditReply
{
    public Guid Id { get; set; }
    public bool Succeeded { get; set; }
    public bool Cancelled { get; set; }
    public string Json { get; set; } = "null";
    public string? Error { get; set; }
}

public sealed class AuditAgentState
{
    public long Sequence { get; set; }
    public AuthenticatedAuditSessionResult? Session { get; set; }
    public AuthenticatedAuditProgressResult? Progress { get; set; }
}

public sealed class AuditWrite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Operation { get; set; } = string.Empty;
    public int RunId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string StartingUrl { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public AuthenticatedAuditStepResult? Step { get; set; }
    public int? LastStepId { get; set; }
    public bool MarkFinal { get; set; }
    public string? Status { get; set; }
    public string? Error { get; set; }
}

