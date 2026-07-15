using CityWebsiteAuditDashboard.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using CityWebsiteAuditDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class WebsiteScansController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebsiteScannerService _scannerService;
    private readonly IWaveAccessibilityService _waveAccessibilityService;

    public WebsiteScansController(
        ApplicationDbContext context,
        IWebsiteScannerService scannerService,
        IWaveAccessibilityService waveAccessibilityService)
    {
        _context = context;
        _scannerService = scannerService;
        _waveAccessibilityService = waveAccessibilityService;
    }

    // GET: WEBSITESCANS
    public async Task<IActionResult> Index(
    string? searchString,
    string? serverFilter,
    string sortBy = "date",
    bool sortDescending = true,
    int page = 1,
    int pageSize = 10,
    int? customPageSize = null)
    {
        if (customPageSize.HasValue)
        {
            pageSize = customPageSize.Value;
        }

        if (pageSize < 1)
        {
            pageSize = 10;
        }

        if (pageSize > 500)
        {
            pageSize = 500;
        }

        IQueryable<WebsiteScan> query = _context.WebsiteScans;

        if (!string.IsNullOrWhiteSpace(searchString))
        {
            query = query.Where(scan =>
                scan.Url.Contains(searchString) ||
                (scan.Notes != null &&
                 scan.Notes.Contains(searchString)));
        }

        if (!string.IsNullOrWhiteSpace(serverFilter))
        {
            query = query.Where(scan =>
                scan.ServerHeader == serverFilter);
        }

        int totalRecords = await query.CountAsync();

        query = sortBy switch
        {
            "url" => sortDescending
                ? query.OrderByDescending(scan => scan.Url)
                : query.OrderBy(scan => scan.Url),

            "status" => sortDescending
                ? query.OrderByDescending(scan => scan.HttpStatusCode)
                : query.OrderBy(scan => scan.HttpStatusCode),

            "response" => sortDescending
                ? query.OrderByDescending(scan => scan.ResponseTimeMilliseconds)
                : query.OrderBy(scan => scan.ResponseTimeMilliseconds),

            _ => sortDescending
                ? query.OrderByDescending(scan => scan.DateScanned)
                : query.OrderBy(scan => scan.DateScanned)
        };

        int totalPages = (int)Math.Ceiling(
            totalRecords / (double)pageSize);

        if (totalPages > 0 && page > totalPages)
        {
            page = totalPages;
        }

        List<WebsiteScan> scans = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        List<string> servers = await _context.WebsiteScans
            .Where(scan => scan.ServerHeader != null)
            .Select(scan => scan.ServerHeader!)
            .Distinct()
            .OrderBy(server => server)
            .ToListAsync();

        WebsiteDashboardViewModel viewModel =
            new WebsiteDashboardViewModel
            {
                Scans = scans,
                SearchString = searchString,
                ServerFilter = serverFilter,
                SortBy = sortBy,
                SortDescending = sortDescending,
                CurrentPage = page,
                TotalPages = totalPages,
                PageSize = pageSize,

                ServerOptions = servers.Select(server =>
                    new SelectListItem
                    {
                        Value = server,
                        Text = server,
                        Selected = server == serverFilter
                    }).ToList(),

                TotalScans = totalRecords,

                SuccessfulScans = await _context.WebsiteScans.CountAsync(scan =>
                    scan.HttpStatusCode >= 200 &&
                    scan.HttpStatusCode < 400),

                FailedScans = await _context.WebsiteScans.CountAsync(scan =>
                    scan.HttpStatusCode == null ||
                    scan.HttpStatusCode >= 400),

                AverageResponseTime = (long)(
                    await _context.WebsiteScans
                        .AverageAsync(scan =>
                            (double?)scan.ResponseTimeMilliseconds)
                    ?? 0)
            };

        return View(viewModel);
    }

    // GET: WEBSITESCANS/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var websitescan = await _context.WebsiteScans
            .FirstOrDefaultAsync(m => m.Id == id);
        if (websitescan == null)
        {
            return NotFound();
        }

        return View(websitescan);
    }

    // GET: WEBSITESCANS/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: WEBSITESCANS/Create
    // To protect from overposting attacks, enable the specific properties you want to bind to.
    // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
    [Bind("Url,Notes")] WebsiteScan websiteScan,
    bool includeWaveScan)

    {
        if (!ModelState.IsValid)
        {
            return View(websiteScan);
        }

        await _scannerService.ScanAsync(websiteScan);

        if (includeWaveScan)
        {
            WaveAccessibilityResult waveResult =
                await _waveAccessibilityService.ScanAsync(websiteScan.Url);

            websiteScan.WaveScanSucceeded = waveResult.Succeeded;
            websiteScan.WaveScannedAt = DateTime.Now;

            if (waveResult.Succeeded)
            {
                websiteScan.WaveErrors = waveResult.Errors;
                websiteScan.WaveContrastErrors = waveResult.ContrastErrors;
                websiteScan.WaveAlerts = waveResult.Alerts;
                websiteScan.WaveFeatures = waveResult.Features;
                websiteScan.WaveAria = waveResult.Aria;
                websiteScan.WaveErrorMessage = null;
            }
            else
            {
                websiteScan.WaveErrors = null;
                websiteScan.WaveContrastErrors = null;
                websiteScan.WaveAlerts = null;
                websiteScan.WaveFeatures = null;
                websiteScan.WaveAria = null;
                websiteScan.WaveErrorMessage = waveResult.ErrorMessage;

                TempData["WaveWarning"] =
                    "The website scan completed, but the accessibility scan could not be completed.";
            }
        }

        _context.Add(websiteScan);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // GET: WEBSITESCANS/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var websitescan = await _context.WebsiteScans.FindAsync(id);
        if (websitescan == null)
        {
            return NotFound();
        }
        return View(websitescan);
    }

    // POST: WEBSITESCANS/Edit/5
    // To protect from overposting attacks, enable the specific properties you want to bind to.
    // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string? notes)
    {
        WebsiteScan? websiteScan =
            await _context.WebsiteScans.FindAsync(id);

        if (websiteScan == null)
        {
            return NotFound();
        }

        websiteScan.Notes = notes;

        await _context.SaveChangesAsync();

        return RedirectToAction(
            nameof(Details),
            new { id = websiteScan.Id });
    }

    // GET: WEBSITESCANS/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var websitescan = await _context.WebsiteScans
            .FirstOrDefaultAsync(m => m.Id == id);
        if (websitescan == null)
        {
            return NotFound();
        }

        return View(websitescan);
    }

    // POST: WEBSITESCANS/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int? id)
    {
        var websitescan = await _context.WebsiteScans.FindAsync(id);
        if (websitescan != null)
        {
            _context.WebsiteScans.Remove(websitescan);
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private bool WebsiteScanExists(int? id)
    {
        return _context.WebsiteScans.Any(e => e.Id == id);
    }

    // POST: WEBSITESCANS/ScanAgain/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScanAgain(int id)
    {
        WebsiteScan? originalScan =
            await _context.WebsiteScans.FindAsync(id);

        if (originalScan == null)
        {
            return NotFound();
        }

        WebsiteScan newScan = new WebsiteScan
        {
            Url = originalScan.Url,
            Notes = originalScan.Notes
        };

        await _scannerService.ScanAsync(newScan);

        _context.WebsiteScans.Add(newScan);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}
