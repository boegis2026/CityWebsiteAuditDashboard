using System.Net;
using System.Net.Http.Json;
using CityWebsiteAuditDashboard.Dtos;
using System.Text.Json;

namespace CityWebsiteAuditDashboard.Services
{
    public class WaveAccessibilityService : IWaveAccessibilityService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WaveAccessibilityService> _logger;

        public WaveAccessibilityService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<WaveAccessibilityService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<WaveAccessibilityResult> ScanAsync(
            string websiteUrl,
            CancellationToken cancellationToken = default)
        {
            string? apiKey = _configuration["WaveApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new WaveAccessibilityResult
                {
                    Succeeded = false,
                    ErrorMessage = "The WAVE API key has not been configured."
                };
            }

            if (!Uri.TryCreate(websiteUrl, UriKind.Absolute, out Uri? websiteUri) ||
                (websiteUri.Scheme != Uri.UriSchemeHttp &&
                 websiteUri.Scheme != Uri.UriSchemeHttps))
            {
                return new WaveAccessibilityResult
                {
                    Succeeded = false,
                    ErrorMessage = "The website URL is not valid."
                };
            }

            try
            {
                string requestUrl =
                    $"https://wave.webaim.org/api/request" +
                    $"?key={Uri.EscapeDataString(apiKey)}" +
                    $"&url={Uri.EscapeDataString(websiteUrl)}" +
                    $"&reporttype=2" +
                    $"&evaldelay=3000";

                using HttpResponseMessage response =
                    await _httpClient.GetAsync(requestUrl, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return new WaveAccessibilityResult
                    {
                        Succeeded = false,
                        ErrorMessage = "The WAVE API key was rejected."
                    };
                }

                if (!response.IsSuccessStatusCode)
                {
                    string responseBody =
                        await response.Content.ReadAsStringAsync(cancellationToken);

                    _logger.LogWarning(
                        "WAVE returned HTTP {StatusCode} for {WebsiteUrl}. Response: {ResponseBody}",
                        (int)response.StatusCode,
                        websiteUrl,
                        responseBody);

                    return new WaveAccessibilityResult
                    {
                        Succeeded = false,
                        ErrorMessage =
                            $"WAVE returned HTTP status {(int)response.StatusCode}."
                    };
                }

                string responseJson =
                    await response.Content.ReadAsStringAsync(cancellationToken);

                WaveApiResponse? waveResponse;

                try
                {
                    waveResponse = JsonSerializer.Deserialize<WaveApiResponse>(
                        responseJson,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                }
                catch (JsonException exception)
                {
                    _logger.LogError(
                        exception,
                        "WAVE JSON could not be parsed for {WebsiteUrl}. " +
                        "JSON path: {JsonPath}. Line: {LineNumber}. Byte: {BytePosition}. " +
                        "Response: {ResponseJson}",
                        websiteUrl,
                        exception.Path,
                        exception.LineNumber,
                        exception.BytePositionInLine,
                        responseJson);

                    return new WaveAccessibilityResult
                    {
                        Succeeded = false,
                        ErrorMessage =
                            $"WAVE returned an unexpected value at {exception.Path ?? "an unknown JSON property"}."
                    };
                }

                if (waveResponse?.Status == null)
                {
                    _logger.LogWarning(
                        "WAVE returned a response without a status object for {WebsiteUrl}.",
                        websiteUrl);

                    return new WaveAccessibilityResult
                    {
                        Succeeded = false,
                        ErrorMessage = "WAVE returned a response without scan status information."
                    };
                }

                if (!waveResponse.Status.Success)
                {
                    string rawWaveError = string.IsNullOrWhiteSpace(waveResponse.Status.Error)
                        ? "WAVE returned no failure explanation."
                        : waveResponse.Status.Error;

                    _logger.LogWarning(
                        "WAVE scan failed for {WebsiteUrl}. HTTP status: {HttpStatusCode}. WAVE message: {WaveError}",
                        websiteUrl,
                        waveResponse.Status.HttpStatusCode,
                        rawWaveError);

                    return new WaveAccessibilityResult
                    {
                        Succeeded = false,
                        ErrorMessage = GetFriendlyWaveError(rawWaveError)
                    };
                }

                if (waveResponse.Categories == null)
                {
                    return new WaveAccessibilityResult
                    {
                        Succeeded = false,
                        ErrorMessage = "WAVE completed the request but did not return accessibility totals."
                    };
                }

                return new WaveAccessibilityResult
                {
                    Succeeded = true,
                    Errors = waveResponse.Categories.Error?.Count ?? 0,
                    ContrastErrors = waveResponse.Categories.Contrast?.Count ?? 0,
                    Alerts = waveResponse.Categories.Alert?.Count ?? 0,
                    Features = waveResponse.Categories.Feature?.Count ?? 0,
                    Aria = waveResponse.Categories.Aria?.Count ?? 0,
                    CreditsRemaining = waveResponse.Statistics?.CreditsRemaining,
                    Issues = BuildIssueList(waveResponse.Categories)
                };
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new WaveAccessibilityResult
                {
                    Succeeded = false,
                    ErrorMessage = "The WAVE accessibility scan timed out."
                };
            }
            catch (HttpRequestException exception)
            {
                _logger.LogError(
                    exception,
                    "A network error occurred while contacting WAVE.");

                return new WaveAccessibilityResult
                {
                    Succeeded = false,
                    ErrorMessage = "The WAVE accessibility service could not be reached."
                };
            }
            catch (JsonException exception)
            {
                _logger.LogError(
                    exception,
                    "WAVE JSON could not be parsed for {WebsiteUrl}.",
                    websiteUrl);

                return new WaveAccessibilityResult
                {
                    Succeeded = false,
                    ErrorMessage =
                        $"JSON parsing failed at: {exception.Path ?? "unknown property"}. " +
                        $"Reason: {exception.Message}"
                };
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "An unexpected error occurred during the WAVE scan.");

                return new WaveAccessibilityResult
                {
                    Succeeded = false,
                    ErrorMessage = "The accessibility scan could not be completed."
                };
            }
        }

        private static List<WaveAccessibilityIssueResult> BuildIssueList(
    WaveCategories categories)
        {
            List<WaveAccessibilityIssueResult> issues = new();

            AddCategoryIssues(issues, "Error", categories.Error);
            AddCategoryIssues(issues, "Contrast", categories.Contrast);
            AddCategoryIssues(issues, "Alert", categories.Alert);
            AddCategoryIssues(issues, "Feature", categories.Feature);
            AddCategoryIssues(issues, "Structure", categories.Structure);
            AddCategoryIssues(issues, "ARIA", categories.Aria);

            return issues
                .Where(issue => issue.Count > 0)
                .OrderBy(issue => issue.Category)
                .ThenByDescending(issue => issue.Count)
                .ThenBy(issue => issue.Description)
                .ToList();
        }

        private static void AddCategoryIssues(
            List<WaveAccessibilityIssueResult> issues,
            string category,
            WaveCategoryCount? categoryResult)
        {
            if (categoryResult == null ||
                categoryResult.Items.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (JsonProperty entry
                in categoryResult.Items.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                WaveApiItem? item;

                try
                {
                    item = entry.Value.Deserialize<WaveApiItem>();
                }
                catch (JsonException)
                {
                    continue;
                }

                if (item == null)
                {
                    continue;
                }

                issues.Add(new WaveAccessibilityIssueResult
                {
                    Category = category,

                    IssueCode = string.IsNullOrWhiteSpace(item.Id)
                        ? entry.Name
                        : item.Id,

                    Description = string.IsNullOrWhiteSpace(item.Description)
                        ? entry.Name.Replace("_", " ")
                        : item.Description,

                    Count = item.Count
                });
            }
        }

        private static string GetFriendlyWaveError(string rawWaveError)
        {
            if (string.IsNullOrWhiteSpace(rawWaveError))
            {
                return "WAVE returned an unspecified error.";
            }

            // map a few common WAVE messages to friendlier text
            string lower = rawWaveError.Trim().ToLowerInvariant();

            if (lower.Contains("invalid api key") || lower.Contains("invalid key"))
            {
                return "The WAVE API key is invalid or has been revoked.";
            }

            if (lower.Contains("quota") || lower.Contains("credits"))
            {
                return "The WAVE API usage limit has been reached.";
            }

            if (lower.Contains("timeout"))
            {
                return "The WAVE service timed out while processing the request.";
            }

            // default: return the original message
            return rawWaveError;
        }
    }
}
