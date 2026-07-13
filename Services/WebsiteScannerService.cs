using CityWebsiteAuditDashboard.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace CityWebsiteAuditDashboard.Services
{
    public class WebsiteScannerService : IWebsiteScannerService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WebsiteScannerService> _logger;

        public WebsiteScannerService(
            HttpClient httpClient,
            ILogger<WebsiteScannerService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task ScanAsync(WebsiteScan websiteScan)
        {
            websiteScan.DateScanned = DateTime.UtcNow;
            websiteScan.ScanError = null;

            _logger.LogInformation(
                "Starting website scan for {Url}",
                websiteScan.Url);

            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                if (!Uri.TryCreate(
                        websiteScan.Url,
                        UriKind.Absolute,
                        out Uri? websiteUri))
                {
                    throw new InvalidOperationException(
                        "The website URL is not valid.");
                }

                if (websiteUri.Scheme != Uri.UriSchemeHttp &&
                    websiteUri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException(
                        "Only HTTP and HTTPS websites can be scanned.");
                }

                IPAddress[] addresses =
                    await Dns.GetHostAddressesAsync(websiteUri.Host);

                IPAddress? ipv4Address = addresses
                    .FirstOrDefault(address =>
                        address.AddressFamily ==
                        AddressFamily.InterNetwork);

                if (ipv4Address == null)
                {
                    throw new InvalidOperationException(
                        "No IPv4 address was found.");
                }

                if (IsPrivateOrLocalAddress(ipv4Address))
                {
                    throw new InvalidOperationException(
                        "Local and private network addresses cannot be scanned.");
                }

                websiteScan.IPv4Address = ipv4Address.ToString();

                using HttpResponseMessage response =
                    await _httpClient.GetAsync(
                        websiteUri,
                        HttpCompletionOption.ResponseHeadersRead);

                websiteScan.HttpStatusCode =
                    (int)response.StatusCode;

                websiteScan.ServerHeader =
                    response.Headers.Server?.ToString();

                websiteScan.XPoweredByHeader =
                    response.Headers.TryGetValues(
                        "X-Powered-By",
                        out IEnumerable<string>? values)
                            ? string.Join(", ", values)
                            : null;

                _logger.LogInformation(
                    "Website scan completed for {Url} with status {StatusCode}",
                    websiteScan.Url,
                    websiteScan.HttpStatusCode);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Website scan failed for {Url}",
                    websiteScan.Url);

                websiteScan.HttpStatusCode = null;
                websiteScan.ScanError = exception.Message;
            }
            finally
            {
                stopwatch.Stop();

                websiteScan.ResponseTimeMilliseconds =
                    stopwatch.ElapsedMilliseconds;
            }
        }

        private static bool IsPrivateOrLocalAddress(
            IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            byte[] bytes = address.GetAddressBytes();

            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 &&
                    bytes[1] >= 16 &&
                    bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }
    }
}