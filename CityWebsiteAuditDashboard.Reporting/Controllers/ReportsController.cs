using System.Diagnostics;
using CityWebsiteAuditDashboard.Reporting.Data;
using CityWebsiteAuditDashboard.Reporting.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityWebsiteAuditDashboard.Reporting.Controllers;

public sealed class ReportsController(ReportStore store) : Controller
{
    private async Task<ReportPage> Prepare(ReportFilter filter, CancellationToken ct)
    {
        var page = new ReportPage { Filter = filter, ValidationError = filter.Resolve() };
        if (!ModelState.IsValid) page.ValidationError = "Check your filters and page number, then try again.";
        if (page.ValidationError is null) page.Applications = await store.Applications(filter, ct);
        else Response.StatusCode = 400;
        return page;
    }
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        var page = await Prepare(filter, ct);
        if (page.ValidationError is null) await store.Overview(page, ct);
        return View(page);
    }
    [HttpGet]
    public async Task<IActionResult> Audits([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        var page = await Prepare(filter, ct);
        if (page.ValidationError is null) await store.History(page, ct);
        return View(page);
    }
    [HttpGet]
    public async Task<IActionResult> Audit(int id, int page = 1, CancellationToken ct = default)
    {
        if (id <= 0 || page < 1 || page > 100000 || !ModelState.IsValid) return BadRequest();
        var model = new ReportPage { Filter = new ReportFilter { Page = page } };
        model.Filter.Resolve();
        await store.Details(model, id, ct);
        return model.Audit is null ? NotFound() : View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Remediation([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        var page = await Prepare(filter, ct);
        if (page.ValidationError is null) await store.Remediation(page, ct);
        return View(page);
    }
    [HttpGet]
    public async Task<IActionResult> AuditPdf(int id, CancellationToken ct)
    {
        if (id <= 0 || !ModelState.IsValid) return BadRequest();
        var page = new ReportPage(); page.Filter.Resolve();
        await store.Details(page, id, ct, all: true);
        if (page.Audit is null) return NotFound();
        return File(Services.ReportPdf.Create(page, true), "application/pdf", $"BOE-Audit-{id}.pdf");
    }
    [HttpGet]
    public async Task<IActionResult> OverviewPdf([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        var page = await Prepare(filter, ct);
        if (page.ValidationError is not null) return BadRequest(page.ValidationError);
        await store.Overview(page, ct);
        return File(Services.ReportPdf.Create(page, false), "application/pdf", "BOE-Accessibility-Overview.pdf");
    }
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Error()
    {
        Response.StatusCode = 500;
        ViewData["Reference"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View();
    }
}
