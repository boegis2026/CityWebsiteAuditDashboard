using CityWebsiteAuditDashboard.Dtos;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CityWebsiteAuditDashboard.Dtos
{
    public class WaveApiResponse
    {
        [JsonPropertyName("status")]
        public WaveStatus? Status { get; set; }

        [JsonPropertyName("categories")]
        public WaveCategories? Categories { get; set; }

        [JsonPropertyName("statistics")]
        public WaveStatistics? Statistics { get; set; }
    }

    public class WaveStatus
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("httpstatuscode")]
        public int? HttpStatusCode { get; set; }
    }

    public class WaveCategories
    {
        [JsonPropertyName("error")]
        public WaveCategoryCount? Error { get; set; }

        [JsonPropertyName("contrast")]
        public WaveCategoryCount? Contrast { get; set; }

        [JsonPropertyName("alert")]
        public WaveCategoryCount? Alert { get; set; }

        [JsonPropertyName("feature")]
        public WaveCategoryCount? Feature { get; set; }

        [JsonPropertyName("structure")]
        public WaveCategoryCount? Structure { get; set; }

        [JsonPropertyName("aria")]
        public WaveCategoryCount? Aria { get; set; }
    }

    public class WaveCategoryCount
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("items")]
        public JsonElement Items { get; set; }
    }

    public class WaveApiItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    public class WaveStatistics
    {
        [JsonPropertyName("creditsremaining")]
        public int? CreditsRemaining { get; set; }

        [JsonPropertyName("time")]
        public decimal? Time { get; set; }

        [JsonPropertyName("pageurl")]
        public string? PageUrl { get; set; }

        [JsonPropertyName("waveurl")]
        public string? WaveUrl { get; set; }
    }
}
