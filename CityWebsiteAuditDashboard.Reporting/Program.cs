using CityWebsiteAuditDashboard.Reporting.Data;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;

PdfSharp.Fonts.GlobalFontSettings.FontResolver = new CityWebsiteAuditDashboard.Reporting.Services.ReportFontResolver();

var builder = WebApplication.CreateBuilder(args);
// Separate from the auditing application's staging.settings.json.
var machineSettings = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "CityWebsiteAuditDashboard", "reporting.settings.json");
builder.Configuration.AddJsonFile(machineSettings, optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables("CITYREPORT_");

var viewers = builder.Configuration.GetSection("Reporting:AllowedUsers").Get<string[]>() ?? [];
var groups = builder.Configuration.GetSection("Reporting:AllowedGroups").Get<string[]>() ?? [];
if (!viewers.Any(x => !string.IsNullOrWhiteSpace(x)) &&
    !groups.Any(x => !string.IsNullOrWhiteSpace(x)))
    throw new InvalidOperationException("Configure Reporting:AllowedUsers or AllowedGroups before starting.");
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("ReportingDatabase")))
    throw new InvalidOperationException("Configure ConnectionStrings:ReportingDatabase before starting.");

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(context =>
            viewers.Contains(context.User.Identity?.Name ?? "", StringComparer.OrdinalIgnoreCase) ||
            groups.Where(x => !string.IsNullOrWhiteSpace(x)).Any(context.User.IsInRole))
        .Build();
});
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<ReportStore>();
var app = builder.Build();

app.UseExceptionHandler("/Reports/Error");
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'none'; style-src 'self'; img-src 'self'; font-src 'self'; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'none'";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseStatusCodePages(async status =>
{
    var response = status.HttpContext.Response;
    response.ContentType = "text/plain; charset=utf-8";
    await response.WriteAsync(response.StatusCode switch
    {
        403 => "Your Windows account is not approved for this reporting site. Contact the dashboard administrator.",
        401 => "Sign in with your approved Windows account.",
        404 => "The requested report was not found.",
        _ => "The request could not be completed."
    });
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Reports}/{action=Index}/{id?}");
app.Run();
