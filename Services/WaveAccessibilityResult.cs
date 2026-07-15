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
    }
}
