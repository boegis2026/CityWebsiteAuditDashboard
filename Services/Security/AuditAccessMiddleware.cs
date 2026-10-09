using System.Net;
using CityWebsiteAuditDashboard.Services.AuditAgent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.IISIntegration;

namespace CityWebsiteAuditDashboard.Services.Security;

public sealed class AuditAccessMiddleware(RequestDelegate next)
{
    public static bool IsLoopbackRequest(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip?.IsIPv4MappedToIPv6 == true) ip = ip.MapToIPv4();
        var host = context.Request.Host.Host.Trim('[', ']');
        bool localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(host, out var hostIp) && IPAddress.IsLoopback(hostIp));
        return ip is not null && IPAddress.IsLoopback(ip) && localHost;
    }

    public async Task InvokeAsync(HttpContext context, LocalDevelopmentAgent local,
        IAuthenticationSchemeProvider schemes, IConfiguration configuration)
    {
        bool hub = context.Request.Path.StartsWithSegments("/hubs/audit-agent");
        if (hub && context.Request.Headers.TryGetValue("Origin", out var origin))
        {
            string expected = $"{context.Request.Scheme}://{context.Request.Host}";
            if (origin.Count != 1 || !string.Equals(origin[0]?.TrimEnd('/'), expected, StringComparison.OrdinalIgnoreCase))
            { await Deny(context, 403, "This Agent connection origin is not allowed."); return; }
        }
        if (local.Enabled)
        {
            if (!IsLoopbackRequest(context))
            { await Deny(context, 403, "Local development is available only on this computer."); return; }
            if (hub)
            {
                var result = await context.AuthenticateAsync(AuditAgentAuthenticationHandler.SchemeName);
                if (!result.Succeeded || result.Principal is null)
                { await Deny(context, 401, "Start this dashboard's automatic local Agent."); return; }
                context.User = result.Principal;
            }
            else context.User = local.CreatePrincipal();
        }
        else
        {
            if (!context.Request.IsHttps)
            { await Deny(context, 403, "HTTPS is required."); return; }
            if (await schemes.GetSchemeAsync(IISDefaults.AuthenticationScheme) is null)
            {
                await Deny(context, 503, "Windows Authentication is not available. Use the local Visual Studio launch profile, or enable Windows Authentication in IIS.");
                return;
            }
            var windows = await context.AuthenticateAsync(IISDefaults.AuthenticationScheme);
            if (!windows.Succeeded || windows.Principal?.Identity?.IsAuthenticated != true)
            { await context.ChallengeAsync(IISDefaults.AuthenticationScheme); return; }
            string[] allowed = configuration.GetSection("Staging:AllowedOperators").Get<string[]>() ?? [];
            if (!allowed.Contains(windows.Principal.Identity.Name ?? "", StringComparer.OrdinalIgnoreCase))
            { await Deny(context, 403, "Your Windows account is not authorized for this dashboard."); return; }
            context.User = AuditOperator.WithOwner(windows.Principal, AuditOperator.WindowsId(windows.Principal));
        }
        await next(context);
    }

    private static async Task Deny(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(message);
    }
}

