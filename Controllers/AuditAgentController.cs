using CityWebsiteAuditDashboard.Hubs;
using CityWebsiteAuditDashboard.Services.AuditAgent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CityWebsiteAuditDashboard.Controllers;

[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class AuditAgentController : Controller
{
    private readonly AuditAgentConnectionRegistry _registry;
    private readonly IHubContext<AuditAgentHub> _hubContext;
    private readonly ILogger<AuditAgentController> _logger;

    public AuditAgentController(
        AuditAgentConnectionRegistry registry,
        IHubContext<AuditAgentHub> hubContext,
        ILogger<AuditAgentController> logger)
    {
        _registry = registry;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(_registry.GetStatus());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenUrl(
        string? startingUrl,
        CancellationToken cancellationToken)
    {
        string value = startingUrl?.Trim() ?? string.Empty;

        if (value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            TempData["AgentOpenUrlStatus"] =
                "Enter a valid HTTP or HTTPS URL without credentials.";
            return RedirectToAction(nameof(Index));
        }

        AuditAgentOpenUrlCommand? command = _registry.BeginOpenUrl();

        if (command is null)
        {
            TempData["AgentOpenUrlStatus"] =
                "The Agent is unavailable or already opening a URL.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _hubContext.Clients.Client(command.ConnectionId)
                .SendAsync(
                    "OpenUrl",
                    command.CommandId,
                    uri.AbsoluteUri,
                    cancellationToken);

            bool opened = await command.Completion.WaitAsync(
                TimeSpan.FromSeconds(20),
                cancellationToken);

            TempData["AgentOpenUrlStatus"] = opened
                ? "The Agent launched Edge with the requested URL."
                : "The Agent could not open the URL. Check its console.";
        }
        catch (TimeoutException)
        {
            TempData["AgentOpenUrlStatus"] =
                "The Agent did not acknowledge the browser request. " +
                "Check its console before trying again.";
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to send browser request {CommandId} to Agent.",
                command.CommandId);

            TempData["AgentOpenUrlStatus"] =
                "The browser request failed. Check the dashboard logs.";
        }
        finally
        {
            _registry.CancelOpenUrl(command.CommandId);
        }

        return RedirectToAction(nameof(Index));
    }
}

