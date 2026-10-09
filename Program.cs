using CityWebsiteAuditDashboard.Services.Security;
using CityWebsiteAuditDashboard.Contracts;
using CityWebsiteAuditDashboard.Services;
using CityWebsiteAuditDashboard.Data;
using Microsoft.EntityFrameworkCore;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using CityWebsiteAuditDashboard.Services.Remediation;
using CityWebsiteAuditDashboard.Hubs;
using CityWebsiteAuditDashboard.Services.AuditAgent;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Machine settings retain the database connection and authorized Windows accounts outside the publish folder.
string machineSettings = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "CityWebsiteAuditDashboard", "staging.settings.json");

builder.Configuration.AddJsonFile(
    machineSettings,
    optional: true,
    reloadOnChange: false);

builder.Configuration.AddEnvironmentVariables();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AuditOperator>();
builder.Services.AddSingleton<LocalDevelopmentAgent>();

builder.Services.AddAuthorization(options =>
    options.AddPolicy(
        AuditOperator.Policy,
        policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(AuditOperator.OwnerClaim)));

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize =
        AuditAgentProtocol.MaximumMessageBytes;

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

// Existing dashboard controls use the connected workstation Agent.
builder.Services.AddSingleton<
    IAuthenticatedAuditService,
    AuthenticatedAuditService>();

// Generates downloadable authenticated accessibility audit reports.
builder.Services.AddScoped<
    IAuthenticatedAuditPdfReportService,
    AuthenticatedAuditPdfReportService>();

// Mark leftover running records interrupted after an application restart.
builder.Services.AddHostedService<
    AuthenticatedAuditStartupRecoveryService>();

builder.Services.AddHostedService<AuditAgentMonitor>();

builder.Services.AddHostedService(provider =>
    provider.GetRequiredService<LocalDevelopmentAgent>());

// Gracefully close active browser sessions when the dashboard stops.
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
app.UseMiddleware<AuditAccessMiddleware>();
app.UseAuthorization();

app.MapStaticAssets();

app.MapHub<AuditAgentHub>("/hubs/audit-agent", options =>
{
    options.ApplicationMaxBufferSize =
        AuditAgentProtocol.MaximumMessageBytes * 2L;

    options.TransportMaxBufferSize =
        AuditAgentProtocol.MaximumMessageBytes * 2L;
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=AccessibilityOverview}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();