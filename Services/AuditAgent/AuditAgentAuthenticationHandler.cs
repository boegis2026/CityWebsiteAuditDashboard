using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CityWebsiteAuditDashboard.Services.AuditAgent;

public sealed class AuditAgentAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AuditAgentKey";
    public const string HeaderName = "X-Audit-Agent-Key";

    private readonly IConfiguration _configuration;

    public AuditAgentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? expected = _configuration["AuditAgent:SharedKey"];

        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32)
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "Agent connection is not configured."));
        }

        bool loopback =
            Context.Connection.RemoteIpAddress is { } address &&
            IPAddress.IsLoopback(address);

        if (!Request.IsHttps && !loopback)
        {
            return Task.FromResult(
                AuthenticateResult.Fail("HTTPS is required."));
        }

        if (!Request.Headers.TryGetValue(HeaderName, out var values) ||
            values.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string supplied = values[0] ?? string.Empty;

        if (supplied.Length > 1024 ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
        {
            return Task.FromResult(
                AuthenticateResult.Fail("Invalid Agent key."));
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    "workstation-agent")
            },
            SchemeName);

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            SchemeName);

        return Task.FromResult(
            AuthenticateResult.Success(ticket));
    }
}
