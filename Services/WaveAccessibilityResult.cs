namespace CityWebsiteAuditDashboard.Services
{
    public class WaveAccessibilityResult
    {
        public bool Succeeded { get; set; }

        public int? Errors { get; set; }

        public int? ContrastErrors { get; set; }

        public int? Alerts { get; set; }

        public int? Features { get; set; }

        public int? Aria { get; set; }

        public int? CreditsRemaining { get; set; }

        public string? ErrorMessage { get; set; }

        public List<WaveAccessibilityIssueResult> Issues { get; set; }
            = new List<WaveAccessibilityIssueResult>();
    }

    public class WaveAccessibilityIssueResult
    {
        public string Category { get; set; } = string.Empty;

        public string IssueCode { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int Count { get; set; }
    }
}
