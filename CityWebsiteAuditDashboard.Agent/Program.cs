using CityWebsiteAuditDashboard.Agent;
using CityWebsiteAuditDashboard.Contracts;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

AgentStartup startup;
try { startup = await AgentStartup.LoadAsync(args); }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
if (startup.ConfigureOnly) return 0;
FileStream instanceLock;
try { instanceLock = startup.AcquireInstanceLock(); }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
using var singleInstance = instanceLock;
var baseUri = new Uri(startup.DashboardUrl);
var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
string LocalTime(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, zone)
    .ToString("MM/dd/yyyy h:mm:ss tt") + " Los Angeles time";
var hubUri = new Uri(new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/"), "hubs/audit-agent");
using var shutdown = new CancellationTokenSource();
Task parentWatch = Task.Run(() => startup.WatchParentAsync(shutdown));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
using var logs = LoggerFactory.Create(builder => builder.AddSimpleConsole(options => options.SingleLine = true));
var logger = logs.CreateLogger("AuditAgent");
Console.WriteLine($"Audit Agent on {Environment.MachineName}. Press Ctrl+C to stop.");
Console.WriteLine($"Connecting to {hubUri}. Full audit protocol v{AuditAgentProtocol.Version}.");
Console.WriteLine(startup.IsLocal ? "Automatic local development Agent." :
    $"Windows account: {Environment.UserDomainName}\\{Environment.UserName}. No pairing key required.");

try
{
    while (!shutdown.IsCancellationRequested)
    {
        // Each registration owns a new engine. A disconnected authenticated browser is never silently resumed.
        using var lease = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        await using var connection = new HubConnectionBuilder().WithUrl(hubUri, options =>
        {
            if (startup.IsLocal) options.Headers["X-Audit-Agent-Key"] = startup.LocalKey!;
            options.UseDefaultCredentials = !startup.IsLocal;
            options.ApplicationMaxBufferSize = AuditAgentProtocol.MaximumMessageBytes * 2L;
            options.TransportMaxBufferSize = AuditAgentProtocol.MaximumMessageBytes * 2L;
        }).Build();
        connection.ServerTimeout = TimeSpan.FromSeconds(30);
        connection.KeepAliveInterval = TimeSpan.FromSeconds(10);
        using var playwright = await Playwright.CreateAsync();
        await using var diagnostic = new AgentBrowser(playwright);
        var persistence = new AgentPersistence(connection, lease.Token);
        var engine = new AgentAuditEngine(persistence, logs.CreateLogger<AgentAuditEngine>());
        await using var worker = new AgentAuditWorker(connection, engine, persistence, diagnostic, logger, lease.Token);
        using var executeSubscription = connection.On<AuditCommand>("ExecuteAudit", worker.Execute);
        using var cancelSubscription = connection.On<Guid, bool>("CancelAudit", worker.Cancel);
        using var openSubscription = connection.On<Guid, string>("OpenUrl", worker.OpenDiagnostic);
        using var expiredSubscription = connection.On("LeaseExpired", () => lease.Cancel());
        connection.Closed += _ => { lease.Cancel(); return Task.CompletedTask; };
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lease.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                await connection.StartAsync(timeout.Token);
                Console.WriteLine(await connection.InvokeAsync<string>("RegisterV2", Environment.MachineName,
                    AuditAgentProtocol.Version, timeout.Token));
            }
            int heartbeatNumber = 0;
            while (!lease.IsCancellationRequested && !worker.MustReconnect)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var serverTime = await connection.InvokeAsync<DateTimeOffset>("ReportState", worker.CaptureState(), timeout.Token);
                if (heartbeatNumber++ % 5 == 0)
                    Console.WriteLine("Heartbeat acknowledged at " + LocalTime(serverTime) + ".");
                await Task.Delay(TimeSpan.FromSeconds(2), lease.Token);
            }
        }
        catch (Exception ex) when (!shutdown.IsCancellationRequested)
        { logger.LogWarning("Connection ended: {Message}. Reconnecting with a new session.", ex.Message); }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally
        {
            // Stop the hub first so MVC marks running records interrupted independently of workstation cleanup.
            lease.Cancel();
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await connection.StopAsync(stopTimeout.Token); }
            catch (Exception ex) { logger.LogWarning(ex, "Connection shutdown warning."); }
        }
        // Await-using disposes the worker before the next registration.
        if (!shutdown.IsCancellationRequested) await Task.Delay(TimeSpan.FromSeconds(5), shutdown.Token);
    }
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
catch (Exception ex) { Console.Error.WriteLine("Agent stopped: " + ex.Message); return 1; }
Console.WriteLine("Audit Agent stopped.");
return 0;

