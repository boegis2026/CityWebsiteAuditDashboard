using CityWebsiteAuditDashboard.Services;
using CityWebsiteAuditDashboard.Data;
using Microsoft.EntityFrameworkCore;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient<IWaveAccessibilityService, WaveAccessibilityService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient<IWebsiteScannerService, WebsiteScannerService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);

    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "CityWebsiteAuditDashboard/1.0");
});

// A singleton is required because the same authenticated Playwright browser
// must remain alive across separate Start, Scan, and Stop HTTP requests.
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
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=WebsiteScans}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
