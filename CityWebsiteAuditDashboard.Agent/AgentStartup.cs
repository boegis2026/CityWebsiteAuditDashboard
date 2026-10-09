using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CityWebsiteAuditDashboard.Agent;

public sealed class AgentStartup
{
    public string DashboardUrl { get; private init; } = "";
    public string? LocalKey { get; private init; }
    public bool ConfigureOnly { get; private init; }
    public bool IsLocal => LocalKey is not null;
    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CityWebsiteAuditDashboard", "Agent");
    private sealed record Settings(string DashboardUrl);
    private sealed record Bootstrap(string DashboardUrl, string Key);

    public static async Task<AgentStartup> LoadAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("The Audit Agent requires Windows.");
        if (args.SequenceEqual(new[] { "--local-bootstrap" }))
        {
            if (!Console.IsInputRedirected) throw new InvalidOperationException("Local startup must be launched by the dashboard.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string input = await Console.In.ReadLineAsync(timeout.Token) ?? throw new InvalidOperationException("Missing local startup data.");
            if (input.Length > 8192) throw new InvalidOperationException("Invalid local startup data.");
            var bootstrap = JsonSerializer.Deserialize<Bootstrap>(input) ?? throw new InvalidOperationException("Invalid local startup data.");
            string url = ValidateUrl(bootstrap.DashboardUrl, allowLoopbackHttp: true);
            if (!new Uri(url).IsLoopback || bootstrap.Key?.Length != 64)
                throw new InvalidOperationException("Local Agent startup requires a local dashboard and an automatic token.");
            return new() { DashboardUrl = url, LocalKey = bootstrap.Key };
        }
        string settingsPath = Path.Combine(SettingsDirectory, "settings.json");
        if (args.Length == 2 && args[0] == "--configure")
        {
            string url = ValidateUrl(args[1]);
            Directory.CreateDirectory(SettingsDirectory);
            string temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Settings(url)));
                File.Move(temporary, settingsPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Console.WriteLine("Saved dashboard address: " + url + ". Windows Authentication will be used.");
            return new() { DashboardUrl = url, ConfigureOnly = true };
        }
        if (args.Length != 0) throw new InvalidOperationException("Usage: Audit Agent [--configure https://your-dashboard-address]");
        if (!File.Exists(settingsPath))
            throw new InvalidOperationException("Install the workstation Agent or run it once with --configure https://your-dashboard-address. Local Visual Studio use starts its Agent automatically.");
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(settingsPath))
            ?? throw new InvalidOperationException("Invalid Agent settings. Run --configure again.");
        return new() { DashboardUrl = ValidateUrl(settings.DashboardUrl) };
    }

    public static string ValidateUrl(string value, bool allowLoopbackHttp = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(allowLoopbackHttp && uri.Scheme == "http" && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Enter the dashboard base HTTPS address, including its application path if applicable.");
        return uri.AbsoluteUri.TrimEnd('/');
    }

    public FileStream AcquireInstanceLock()
    {
        Directory.CreateDirectory(SettingsDirectory);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DashboardUrl.ToUpperInvariant())));
        try { return new FileStream(Path.Combine(SettingsDirectory, "running-" + hash + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("An Agent for this dashboard is already running under your Windows account."); }
    }

    public async Task WatchParentAsync(CancellationTokenSource shutdown)
    {
        if (!IsLocal) return;
        try
        {
            // The parent keeps its pipe open. Normal shutdown and a parent crash both close it.
            while (await Console.In.ReadLineAsync(shutdown.Token) is not null) { }
            await shutdown.CancelAsync();
        }
        catch (OperationCanceledException) { }
        catch (IOException) { await shutdown.CancelAsync(); }
    }
}

