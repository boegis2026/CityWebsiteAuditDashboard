using CityWebsiteAuditDashboard.Services.AuditAgent;
using Microsoft.AspNetCore.Mvc;

namespace CityWebsiteAuditDashboard.Controllers;

[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class AuditAgentController : Controller
{
    private readonly AuditAgentConnectionRegistry _registry;

    public AuditAgentController(
        AuditAgentConnectionRegistry registry)
    {
        _registry = registry;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(_registry.GetStatus());
    }
}
