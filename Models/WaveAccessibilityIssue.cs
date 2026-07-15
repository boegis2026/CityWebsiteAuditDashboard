using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.Models
{
    public class WaveAccessibilityIssue
    {
        public int Id { get; set; }

        public int WebsiteScanId { get; set; }

        [Required]
        [StringLength(50)]
        public string Category { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string IssueCode { get; set; } = string.Empty;

        [Required]
        [StringLength(300)]
        public string Description { get; set; } = string.Empty;

        public int Count { get; set; }

        public WebsiteScan? WebsiteScan { get; set; }
    }
}
