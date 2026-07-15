using System.Net;
using System.Net.Http.Json;
using CityWebsiteAuditDashboard.Dtos;

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
                    $"&reporttype=2";

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
                    _logger.LogWarning(
                        "WAVE returned HTTP status code {StatusCode}.",
                        response.StatusCode);

                    return new WaveAccessibilityResult
                    {
                        Succeeded = false,
                        ErrorMessage = "The WAVE accessibility service could not complete the scan."
                    };
                }

                WaveApiResponse? waveResponse =
                    await response.Content.ReadFromJsonAsync<WaveApiResponse>(
                        cancellationToken: cancellationToken);

                if (waveResponse?.Status == null)
                {
                    return new WaveAccessibilityResult
                    {
                        Succeeded = false,
                        ErrorMessage = "WAVE returned an unexpected response."
                    };
                }

                if (!waveResponse.Status.Success)
                {
                    _logger.LogWarning(
                        "WAVE scan failed. Message: {WaveError}",
                        waveResponse.Status.Error);

                    return new WaveAccessibilityResult
                    {
                        Succeeded = false,
                        ErrorMessage = GetFriendlyWaveError(waveResponse.Status.Error)
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
            if (categoryResult?.Items == null)
            {
                return;
            }

            foreach (KeyValuePair<string, WaveApiItem> entry
                in categoryResult.Items)
            {
                WaveApiItem item = entry.Value;

                issues.Add(new WaveAccessibilityIssueResult
                {
                    Category = category,

                    IssueCode = string.IsNullOrWhiteSpace(item.Id)
                        ? entry.Key
                        : item.Id,

                    Description = string.IsNullOrWhiteSpace(item.Description)
                        ? entry.Key.Replace("_", " ")
                        : item.Description,

                    Count = item.Count
                });
            }
        }
        private static string GetFriendlyWaveError(string? waveError)
        {
            if (string.IsNullOrWhiteSpace(waveError))
            {
                return "The WAVE accessibility scan could not be completed.";
            }

            string lowerError = waveError.ToLowerInvariant();

            if (lowerError.Contains("credit"))
            {
                return "The WAVE account does not have enough API credits.";
            }

            if (lowerError.Contains("key") ||
                lowerError.Contains("authentication") ||
                lowerError.Contains("authorized"))
            {
                return "The WAVE API key was rejected.";
            }

            if (lowerError.Contains("url"))
            {
                return "WAVE could not scan the supplied website URL.";
            }

            return "The WAVE accessibility scan could not be completed.";
        }
    }
}
