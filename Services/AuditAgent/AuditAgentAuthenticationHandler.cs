using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using CityWebsiteAuditDashboard.Services.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CityWebsiteAuditDashboard.Services.AuditAgent;

// Only the automatic local child uses this short-lived token. Old shared keys are not accepted.
public sealed class AuditAgentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, LocalDevelopmentAgent local)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "LocalDevelopmentAgent";
    public const string HeaderName = "X-Audit-Agent-Key";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!local.Enabled || !AuditAccessMiddleware.IsLoopbackRequest(Context))
            return Task.FromResult(AuthenticateResult.NoResult());
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1)
            return Task.FromResult(AuthenticateResult.NoResult());
        string supplied = values[0] ?? "";
        if (supplied.Length != 64 || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(local.BootstrapKey)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            return Task.FromResult(AuthenticateResult.Fail("Invalid local Agent token."));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(local.CreatePrincipal(), SchemeName)));
    }
}
