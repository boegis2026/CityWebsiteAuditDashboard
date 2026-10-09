using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CityWebsiteAuditDashboard.Services.Security;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace CityWebsiteAuditDashboard.Services.AuditAgent;

public sealed class LocalDevelopmentAgent : BackgroundService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServer _server;
    private readonly ILogger<LocalDevelopmentAgent> _logger;
    public bool Enabled { get; }
    public string BootstrapKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string Status { get; private set; } = "Local automatic Agent is disabled.";

    public LocalDevelopmentAgent(IWebHostEnvironment environment, IConfiguration configuration,
        IHostApplicationLifetime lifetime, IServer server, ILogger<LocalDevelopmentAgent> logger)
    {
        _environment = environment; _lifetime = lifetime; _server = server; _logger = logger;
        bool iis = server.GetType().FullName?.Contains("IIS", StringComparison.OrdinalIgnoreCase) == true ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_PORT")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH"));
        Enabled = OperatingSystem.IsWindows() && environment.IsDevelopment() && !iis &&
            configuration.GetValue<bool>("AuditAgent:LocalDevelopment") &&
            File.Exists(Path.Combine(environment.ContentRootPath, "CityWebsiteAuditDashboard.csproj"));
        if (Enabled) Status = "Waiting for the local dashboard to start.";
    }

    public ClaimsPrincipal CreatePrincipal() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, Environment.UserDomainName + "\\" + Environment.UserName),
         new Claim(AuditOperator.OwnerClaim, "local-development")], "LocalDevelopment"));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled) return;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try { await started.Task.WaitAsync(stoppingToken); }
        catch (OperationCanceledException) { return; }
        string? url = _server.Features.Get<IServerAddressesFeature>()?.Addresses
            .Select(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) ? uri : null)
            .Where(x => x is not null && x.IsLoopback && (x.Scheme == "https" || x.Scheme == "http"))
            .OrderByDescending(x => x!.Scheme == "https").FirstOrDefault()?.AbsoluteUri;
        if (url is null)
        { Status = "Select the local https launch profile and use localhost bindings."; _logger.LogError("{Status}", Status); return; }
        var metadata = typeof(LocalDevelopmentAgent).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        string configuration = metadata.FirstOrDefault(x => x.Key == "AgentBuildConfiguration")?.Value ?? "Debug";
        string agent = Path.Combine(_environment.ContentRootPath, "CityWebsiteAuditDashboard.Agent", "bin",
            configuration, "net10.0-windows", "CityWebsiteAuditDashboard.Agent.dll");
        if (!File.Exists(agent))
        { Status = "Agent output is missing. Rebuild the solution, then start the dashboard again."; _logger.LogError("{Status}", Status); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            using var process = new Process();
            try
            {
                var info = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(agent)!
                };
                info.ArgumentList.Add(agent);
                info.ArgumentList.Add("--local-bootstrap");
                info.Environment.Remove("AuditAgent__SharedKey");
                info.Environment.Remove("AuditAgent__DashboardUrl");
                process.StartInfo = info;
                if (!process.Start()) throw new InvalidOperationException("Could not start local Agent process.");
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { DashboardUrl = url, Key = BootstrapKey }));
                await process.StandardInput.FlushAsync();
                Status = "Local Agent started. Check connection status below.";
                Task output = ForwardOutput(process.StandardOutput, false);
                Task errors = ForwardOutput(process.StandardError, true);
                await process.WaitForExitAsync(stoppingToken);
                await Task.WhenAll(output, errors);
                Status = "Local Agent stopped; restarting shortly.";
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            { Status = "Local Agent could not start. Check Visual Studio's application output."; _logger.LogError(ex, "Local Agent startup failed."); }
            finally
            {
                try
                {
                    if (process.Id > 0 && !process.HasExited)
                    {
                        process.StandardInput.Close();
                        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); }
                        catch (TimeoutException) { process.Kill(entireProcessTree: true); }
                    }
                }
                catch (InvalidOperationException) { }
            }
            if (!stoppingToken.IsCancellationRequested)
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { }
        }
        Status = "Local Agent stopped with the dashboard.";
    }

    private async Task ForwardOutput(StreamReader reader, bool error)
    {
        while (await reader.ReadLineAsync() is { } line)
            if (error) _logger.LogWarning("Agent: {Line}", line);
            else _logger.LogInformation("Agent: {Line}", line);
    }
}
