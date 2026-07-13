using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.Models
{
    public class WebsiteScan
    {
        public int Id { get; set; }

        [Required]
        [Url]
        [Display(Name = "Website URL")]
        public string Url { get; set; } = string.Empty;

        [Display(Name = "HTTP Status")]
        public int? HttpStatusCode { get; set; }

        [Display(Name = "Response Time (ms)")]
        public long? ResponseTimeMilliseconds { get; set; }

        [Display(Name = "Server")]
        public string? ServerHeader { get; set; }

        [Display(Name = "X-Powered-By")]
        public string? XPoweredByHeader { get; set; }

        [Display(Name = "IPv4 Address")]
        public string? IPv4Address { get; set; }

        [Display(Name = "Date Scanned")]
        public DateTime DateScanned { get; set; }

        public string? Notes { get; set; }

        [Display(Name = "Scan Error")]
        public string? ScanError { get; set; }
    }
}
