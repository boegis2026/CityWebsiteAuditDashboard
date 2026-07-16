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

        var successfulWaveScans = await _context.WebsiteScans
            .Where(scan => scan.WaveScanSucceeded == true)
            .Select(scan => new
            {
                scan.Url,
                WaveErrors = scan.WaveErrors ?? 0,
                WaveContrastErrors = scan.WaveContrastErrors ?? 0
            })
            .ToListAsync();

        int totalWaveErrors = successfulWaveScans
            .Sum(scan => scan.WaveErrors);

        int totalWaveContrastErrors = successfulWaveScans
            .Sum(scan => scan.WaveContrastErrors);

        int websitesWithWaveErrors = successfulWaveScans
            .Count(scan =>
                scan.WaveErrors > 0 ||
                scan.WaveContrastErrors > 0);

        var highestWaveErrorWebsite = successfulWaveScans
            .OrderByDescending(scan =>
                scan.WaveErrors + scan.WaveContrastErrors)
            .FirstOrDefault();

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
                    ?? 0),

                TotalWaveErrors = totalWaveErrors,
                TotalWaveContrastErrors = totalWaveContrastErrors,
                WebsitesWithWaveErrors = websitesWithWaveErrors,
                WebsiteWithHighestWaveErrors = highestWaveErrorWebsite?.Url,

                HighestWaveErrorCount = highestWaveErrorWebsite == null
                    ? 0
                    : highestWaveErrorWebsite.WaveErrors +
                    highestWaveErrorWebsite.WaveContrastErrors
            };

        return View(viewModel);
    }

    // GET: WebsiteScans/Batch
    [HttpGet]
    public IActionResult Batch()
    {
        return View(new BatchScanViewModel());
    }

    // POST: WebsiteScans/Batch
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Batch(BatchScanViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        List<string> urls = model.UrlsText
            .Split(
                new[] { "\r\n", "\n", "\r" },
                StringSplitOptions.None)
            .Select(url => url.Trim())
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();

        if (urls.Count == 0)
        {
            ModelState.AddModelError(
                nameof(model.UrlsText),
                "Paste at least one website URL.");

            return View(model);
        }

        if (urls.Count > 25)
        {
            ModelState.AddModelError(
                nameof(model.UrlsText),
                "A batch cannot contain more than 25 URLs.");

            return View(model);
        }

        if (model.NumberOfUrls != urls.Count)
        {
            ModelState.AddModelError(
                nameof(model.NumberOfUrls),
                $"You entered {model.NumberOfUrls}, but {urls.Count} non-empty URL(s) were pasted.");

            return View(model);
        }

        List<string> invalidUrls = new();

        foreach (string url in urls)
        {
            bool isValid =
                Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri) &&
                !string.IsNullOrWhiteSpace(parsedUri.Host) &&
                (parsedUri.Scheme == Uri.UriSchemeHttp ||
                 parsedUri.Scheme == Uri.UriSchemeHttps);

            if (!isValid)
            {
                invalidUrls.Add(url);
            }
        }

        if (invalidUrls.Count > 0)
        {
            ModelState.AddModelError(
                nameof(model.UrlsText),
                "These URLs are invalid: " +
                string.Join(", ", invalidUrls));

            return View(model);
        }

        model.UrlsText = string.Join(
            Environment.NewLine,
            urls);

        model.Results.Clear();
        model.TotalSubmitted = urls.Count;
        model.SuccessfulScans = 0;
        model.FailedScans = 0;
        model.WaveScansCompleted = 0;
        model.WaveScansFailed = 0;

        foreach (string url in urls)
        {
            BatchScanResultViewModel result =
                new BatchScanResultViewModel
                {
                    Url = url,
                    WaveRequested = model.IncludeWaveScan
                };

            WebsiteScan? websiteScan = null;

            bool normalScanSucceeded = false;
            bool recordSaved = false;
            bool? waveSucceeded = null;

            try
            {
                websiteScan = new WebsiteScan
                {
                    Url = url
                };

                // Reuse the existing normal website scanner.
                await _scannerService.ScanAsync(websiteScan);

                normalScanSucceeded =
                    string.IsNullOrWhiteSpace(websiteScan.ScanError);

                if (normalScanSucceeded)
                {
                    result.Message = "Website scan completed.";
                }
                else
                {
                    result.Message =
                        string.IsNullOrWhiteSpace(websiteScan.ScanError)
                            ? "The website scan could not be completed."
                            : websiteScan.ScanError;
                }

                // Run WAVE only when deliberately selected.
                if (model.IncludeWaveScan)
                {
                    WaveAccessibilityResult waveResult =
                        await _waveAccessibilityService.ScanAsync(url);

                    waveSucceeded = waveResult.Succeeded;
                    result.WaveSucceeded = waveResult.Succeeded;

                    websiteScan.WaveScanSucceeded =
                        waveResult.Succeeded;

                    websiteScan.WaveScannedAt =
                        DateTime.Now;

                    if (waveResult.Succeeded)
                    {
                        websiteScan.WaveErrors =
                            waveResult.Errors;

                        websiteScan.WaveContrastErrors =
                            waveResult.ContrastErrors;

                        websiteScan.WaveAlerts =
                            waveResult.Alerts;

                        websiteScan.WaveFeatures =
                            waveResult.Features;

                        websiteScan.WaveAria =
                            waveResult.Aria;

                        websiteScan.WaveErrorMessage = null;

                        foreach (WaveAccessibilityIssueResult issue
                            in waveResult.Issues)
                        {
                            websiteScan.WaveAccessibilityIssues.Add(
                                new WaveAccessibilityIssue
                                {
                                    Category = issue.Category,
                                    IssueCode = issue.IssueCode,
                                    Description = issue.Description,
                                    Count = issue.Count
                                });
                        }

                        result.Message +=
                            " WAVE accessibility scan completed.";
                    }
                    else
                    {
                        websiteScan.WaveErrors = null;
                        websiteScan.WaveContrastErrors = null;
                        websiteScan.WaveAlerts = null;
                        websiteScan.WaveFeatures = null;
                        websiteScan.WaveAria = null;

                        websiteScan.WaveErrorMessage =
                            waveResult.ErrorMessage;

                        result.Message +=
                            " WAVE accessibility scan failed.";

                        if (!string.IsNullOrWhiteSpace(
                            waveResult.ErrorMessage))
                        {
                            result.Message +=
                                $" {waveResult.ErrorMessage}";
                        }
                    }
                }

                // Save this URL independently from every other URL.
                _context.WebsiteScans.Add(websiteScan);
                await _context.SaveChangesAsync();

                recordSaved = true;

                // EF Core fills this after the INSERT succeeds.
                result.WebsiteScanId = websiteScan.Id;
            }
            catch (Exception)
            {
                /*
                 * If an INSERT fails, remove any unsaved Added entities
                 * from EF Core's tracker. Otherwise, EF may try to save
                 * them again during the next URL.
                 */
                foreach (var entry in _context.ChangeTracker
                    .Entries()
                    .Where(entry =>
                        entry.State ==
                        Microsoft.EntityFrameworkCore.EntityState.Added)
                    .ToList())
                {
                    entry.State =
                        Microsoft.EntityFrameworkCore.EntityState.Detached;
                }

                result.Message =
                    "This URL could not be fully processed or saved. " +
                    "The remaining URLs were still processed.";

                if (model.IncludeWaveScan &&
                    waveSucceeded == null)
                {
                    waveSucceeded = false;
                    result.WaveSucceeded = false;
                }
            }

            /*
             * A batch item counts as successful only when the normal
             * scan succeeded and its database record was saved.
             */
            result.ScanSucceeded =
                normalScanSucceeded && recordSaved;

            if (result.ScanSucceeded)
            {
                model.SuccessfulScans++;
            }
            else
            {
                model.FailedScans++;
            }

            if (model.IncludeWaveScan)
            {
                if (waveSucceeded == true)
                {
                    model.WaveScansCompleted++;
                }
                else
                {
                    model.WaveScansFailed++;
                }
            }

            model.Results.Add(result);
        }

        model.BatchCompleted = true;

        return View(model);
    }

    // GET: WEBSITESCANS/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var websitescan = await _context.WebsiteScans
            .Include(scan => scan.WaveAccessibilityIssues)
            .FirstOrDefaultAsync(scan => scan.Id == id);

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

            foreach (WaveAccessibilityIssueResult issue in waveResult.Issues)
{
    websiteScan.WaveAccessibilityIssues.Add(
        new WaveAccessibilityIssue
        {
            Category = issue.Category,
            IssueCode = issue.IssueCode,
            Description = issue.Description,
            Count = issue.Count
        });
}
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

    // POST: WebsiteScans/ScanAgain/5
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
            Notes = originalScan.Notes,

            WaveErrors = originalScan.WaveErrors,
            WaveContrastErrors = originalScan.WaveContrastErrors,
            WaveAlerts = originalScan.WaveAlerts,
            WaveFeatures = originalScan.WaveFeatures,
            WaveAria = originalScan.WaveAria,
            WaveScanSucceeded = originalScan.WaveScanSucceeded,
            WaveErrorMessage = originalScan.WaveErrorMessage,
            WaveScannedAt = originalScan.WaveScannedAt
        };

        await _scannerService.ScanAsync(newScan);

        _context.WebsiteScans.Add(newScan);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}
