using Microsoft.Playwright;

namespace CityWebsiteAuditDashboard.Agent;

public sealed class AgentBrowser : IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _disposed;

    public AgentBrowser(IPlaywright playwright)
    {
        _playwright = playwright;
    }

    public async Task OpenAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            url.Length > 2048 ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? target) ||
            (target.Scheme != Uri.UriSchemeHttp &&
             target.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(target.UserInfo))
        {
            throw new ArgumentException(
                "The dashboard sent an invalid URL.");
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            if (_browser is null ||
                !_browser.IsConnected ||
                _context is null ||
                _page is null ||
                _page.IsClosed)
            {
                await CloseCoreAsync();

                _browser = await _playwright.Chromium.LaunchAsync(
                    new BrowserTypeLaunchOptions
                    {
                        Channel = "msedge",
                        Headless = false,
                        Timeout = 30_000
                    });

                cancellationToken.ThrowIfCancellationRequested();

                _context = await _browser.NewContextAsync();
                _page = await _context.NewPageAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();

            await _page.GotoAsync(
                target.AbsoluteUri,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30_000
                });

            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine(
                "Agent-controlled Edge navigation completed.");
        }
        catch
        {
            await CloseCoreAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync()
    {
        await _gate.WaitAsync();

        try
        {
            await CloseCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CloseCoreAsync()
    {
        IBrowser? browser = _browser;

        _browser = null;
        _context = null;
        _page = null;

        if (browser is not null)
        {
            try
            {
                await browser.CloseAsync();
            }
            catch (PlaywrightException)
            {
                Console.Error.WriteLine(
                    "Edge was already disconnected during cleanup.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();

        try
        {
            _disposed = true;
            await CloseCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}
