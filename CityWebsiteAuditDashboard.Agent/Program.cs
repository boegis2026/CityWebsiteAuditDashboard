using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;

var losAngelesTimeZone =
    TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

string FormatLosAngelesTime(DateTimeOffset timestamp)
{
    return TimeZoneInfo.ConvertTime(
        timestamp,
        losAngelesTimeZone
    ).ToString("MM/dd/yyyy h:mm:ss tt") + " Los Angeles time";
}

string? dashboardUrl =
    Environment.GetEnvironmentVariable(
        "AuditAgent__DashboardUrl");

string? sharedKey =
    Environment.GetEnvironmentVariable(
        "AuditAgent__SharedKey");

if (!Uri.TryCreate(
        dashboardUrl,
        UriKind.Absolute,
        out Uri? baseUri) ||
    (baseUri.Scheme != Uri.UriSchemeHttps &&
     !(baseUri.Scheme == Uri.UriSchemeHttp &&
       baseUri.IsLoopback)) ||
    !string.IsNullOrEmpty(baseUri.UserInfo) ||
    !string.IsNullOrEmpty(baseUri.Query) ||
    !string.IsNullOrEmpty(baseUri.Fragment))
{
    Console.Error.WriteLine(
        "Set AuditAgent__DashboardUrl to the dashboard base URL. " +
        "Use HTTPS, or HTTP on localhost only.");

    return 1;
}

if (string.IsNullOrWhiteSpace(sharedKey) ||
    sharedKey.Length < 32 ||
    sharedKey.Length > 1024)
{
    Console.Error.WriteLine(
        "Set AuditAgent__SharedKey to the same random key " +
        "configured on the dashboard (at least 32 characters).");

    return 1;
}

// Preserve an IIS application path such as /AuditDashboard/.
var hubUri = new Uri(
    new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/"),
    "hubs/audit-agent");

using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

await using var connection = new HubConnectionBuilder()
    .WithUrl(hubUri, options =>
    {
        options.Headers["X-Audit-Agent-Key"] = sharedKey;

        // Supports IIS sites that also require the logged-in
        // Windows identity.
        options.UseDefaultCredentials = true;
    })
    .Build();

connection.On<string>(
    "DashboardHello",
    message => Console.WriteLine(message));

connection.On<Guid, string>("OpenUrl", async (commandId, url) =>
{
    bool opened = false;

    try
    {
        if (shutdown.IsCancellationRequested)
        {
            return;
        }

        if (url.Length > 2048 ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? target) ||
            (target.Scheme != Uri.UriSchemeHttp &&
             target.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(target.UserInfo))
        {
            throw new ArgumentException("The dashboard sent an invalid URL.");
        }

        string? edgePath = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "Application", "msedge.exe")
        }.FirstOrDefault(File.Exists);

        if (edgePath is null)
        {
            throw new FileNotFoundException("Microsoft Edge was not found on this workstation.");
        }

        var startInfo = new ProcessStartInfo(edgePath)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--new-window");
        startInfo.ArgumentList.Add(target.AbsoluteUri);

        using Process? edge = Process.Start(startInfo);
        if (edge is null)
        {
            throw new InvalidOperationException("Microsoft Edge did not start.");
        }

        opened = true;
        Console.WriteLine($"Opened Edge for {target.Host}.");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Could not open Edge: {exception.Message}");
    }

    try
    {
        using var acknowledgementTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        acknowledgementTimeout.CancelAfter(TimeSpan.FromSeconds(10));

        bool accepted = await connection.InvokeAsync<bool>(
            "CompleteOpenUrl",
            commandId,
            opened,
            acknowledgementTimeout.Token);

        if (!accepted)
        {
            Console.Error.WriteLine("The dashboard no longer expects this browser request.");
        }
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
    {
        // The Agent is stopping.
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Could not acknowledge browser request: {exception.Message}");
    }
});

connection.Closed += _ =>
{
    Console.WriteLine(
        $"Dashboard connection closed at " +
        $"{FormatLosAngelesTime(DateTimeOffset.UtcNow)}.");

    return Task.CompletedTask;
};

Console.WriteLine(
    $"Audit Agent on {Environment.MachineName}. " +
    "Press Ctrl+C to stop.");

Console.WriteLine($"Connecting to {hubUri}");

Console.WriteLine(
    "Open URL is enabled. Playwright remains in the dashboard.");

// One loop owns initial connection, registration and reconnection.
// Individual connection attempts and heartbeats have bounded timeouts.
try
{
    while (!shutdown.IsCancellationRequested)
    {
        try
        {
            using (var connectTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    shutdown.Token))
            {
                connectTimeout.CancelAfter(
                    TimeSpan.FromSeconds(15));

                await connection.StartAsync(
                    connectTimeout.Token);

                string reply =
                    await connection.InvokeAsync<string>(
                        "Register",
                        Environment.MachineName,
                        connectTimeout.Token);

                Console.WriteLine(reply);
            }

            while (!shutdown.IsCancellationRequested)
            {
                using var heartbeatTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        shutdown.Token);

                heartbeatTimeout.CancelAfter(
                    TimeSpan.FromSeconds(10));

                DateTimeOffset serverTime =
                    await connection.InvokeAsync<DateTimeOffset>(
                        "Heartbeat",
                        heartbeatTimeout.Token);

                Console.WriteLine(
                    "Heartbeat acknowledged at " +
                    FormatLosAngelesTime(serverTime) + ".");

                await Task.Delay(
                    TimeSpan.FromSeconds(10),
                    shutdown.Token);
            }
        }
        catch (OperationCanceledException)
            when (shutdown.IsCancellationRequested)
        {
            break;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Connection unavailable: {exception.Message}");

            Console.WriteLine(
                "Retrying in 5 seconds. Check the URL, " +
                "matching key, and dashboard availability.");
        }
        finally
        {
            using var stopTimeout =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(5));

            try
            {
                await connection.StopAsync(
                    stopTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                // The bounded shutdown wait expired.
            }
        }

        await Task.Delay(
            TimeSpan.FromSeconds(5),
            shutdown.Token);
    }
}
catch (OperationCanceledException)
    when (shutdown.IsCancellationRequested)
{
    // Ctrl+C ends the retry or heartbeat delay.
}

Console.WriteLine("Audit Agent stopped.");

return 0;
