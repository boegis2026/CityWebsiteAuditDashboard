using System.Net;
using CityWebsiteAuditDashboard.Contracts;
using CityWebsiteAuditDashboard.Services;
using CityWebsiteAuditDashboard.Data;
using Microsoft.EntityFrameworkCore;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using CityWebsiteAuditDashboard.Services.Remediation;
using CityWebsiteAuditDashboard.Hubs;
using CityWebsiteAuditDashboard.Services.AuditAgent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.IISIntegration;

var builder = WebApplication.CreateBuilder(args);

// Machine settings live outside the publish folder so publishing cannot erase the pairing key.
string machineSettings = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "CityWebsiteAuditDashboard", "staging.settings.json");
builder.Configuration.AddJsonFile(machineSettings, optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = AuditAgentProtocol.MaximumMessageBytes;
    // Saving a step must not block the Agent's heartbeat or stop acknowledgement.
    options.MaximumParallelInvocationsPerClient = 4;
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<AuditAgentDispatcher>();
builder.Services.AddSingleton<AuditAgentStore>();

builder.Services.AddSingleton<AuditAgentConnectionRegistry>();

builder.Services.AddAuthentication()
    .AddScheme<
        AuthenticationSchemeOptions,
        AuditAgentAuthenticationHandler>(
            AuditAgentAuthenticationHandler.SchemeName,
            _ => { });

builder.Services.AddHttpClient<
    IWaveAccessibilityService,
    WaveAccessibilityService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(60);
    });

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString(
            "DefaultConnection")));

builder.Services.AddHttpClient<
    IWebsiteScannerService,
    WebsiteScannerService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "CityWebsiteAuditDashboard/1.0");
    });

builder.Services.AddScoped<AccessibilityRemediationService>();

builder.Services.AddScoped<AccessibilityRemediationMatcher>();

builder.Services.AddScoped<AccessibilityRemediationRetestService>();

builder.Services.AddScoped<
    AccessibilityRemediationWorkflowComparisonService>();

// The singleton routes existing dashboard controls to the connected workstation Agent.
builder.Services.AddSingleton<
    IAuthenticatedAuditService,
    AuthenticatedAuditService>();

// Generates downloadable authenticated accessibility audit reports.
builder.Services.AddScoped<
    IAuthenticatedAuditPdfReportService,
    AuthenticatedAuditPdfReportService>();

/*
 * A Playwright browser session cannot survive an application restart.
 * This startup service marks any leftover Running database records as
 * Interrupted so the history page does not show sessions that no longer exist.
 */
builder.Services.AddHostedService<
    AuthenticatedAuditStartupRecoveryService>();
builder.Services.AddHostedService<AuditAgentMonitor>();

/*
 * Gracefully closes active Playwright browsers when the dashboard stops.
 * Startup recovery remains responsible for sessions lost during crashes or
 * forced process termination.
 */
builder.Services.AddHostedService<
    AuthenticatedAuditShutdownService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Local development keeps working. Remote dashboard access requires an authenticated
// operator explicitly listed in the machine settings; the hub separately requires its pairing key.
app.Use(async (context, next) =>
{
    bool hubRequest = context.Request.Path
        .StartsWithSegments("/hubs/audit-agent");

    bool loopback = context.Connection.RemoteIpAddress is { } ip
        && IPAddress.IsLoopback(ip);

    bool requireLocalSignIn = app.Configuration
        .GetValue<bool>("Staging:RequireWindowsAuthenticationLocally");

    if (!hubRequest && (!loopback || requireLocalSignIn))
    {
        if (!context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("HTTPS is required.");
            return;
        }

        var windows = await context.AuthenticateAsync(
            IISDefaults.AuthenticationScheme);

        if (!windows.Succeeded ||
            windows.Principal?.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync(
                IISDefaults.AuthenticationScheme);
            return;
        }

        context.User = windows.Principal;

        string[] operators = app.Configuration
            .GetSection("Staging:AllowedOperators")
            .Get<string[]>() ?? [];

        if (!operators.Contains(
            context.User.Identity?.Name ?? string.Empty,
            StringComparer.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(
                "Your Windows account is not authorized for this dashboard.");
            return;
        }
    }

    await next(context);
});

app.MapStaticAssets();

app.MapHub<AuditAgentHub>("/hubs/audit-agent", options =>
{
    options.ApplicationMaxBufferSize = AuditAgentProtocol.MaximumMessageBytes * 2L;
    options.TransportMaxBufferSize = AuditAgentProtocol.MaximumMessageBytes * 2L;
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=AccessibilityOverview}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
