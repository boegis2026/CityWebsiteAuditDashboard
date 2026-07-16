using System.ComponentModel.DataAnnotations;

namespace CityWebsiteAuditDashboard.ViewModels
{
    public class BatchScanViewModel
    {
        [Required(ErrorMessage = "Enter the number of URLs.")]
        [Range(1, 25, ErrorMessage = "The batch must contain between 1 and 25 URLs.")]
        [Display(Name = "Number of URLs")]
        public int? NumberOfUrls { get; set; }

        [Required(ErrorMessage = "Paste at least one website URL.")]
        [Display(Name = "Website URLs")]
        public string UrlsText { get; set; } = string.Empty;

        [Display(Name = "Include WAVE accessibility scans")]
        public bool IncludeWaveScan { get; set; }

        public bool BatchCompleted { get; set; }

        public int TotalSubmitted { get; set; }

        public int SuccessfulScans { get; set; }

        public int FailedScans { get; set; }

        public int WaveScansCompleted { get; set; }

        public int WaveScansFailed { get; set; }

        public List<BatchScanResultViewModel> Results { get; set; }
            = new List<BatchScanResultViewModel>();
    }

    public class BatchScanResultViewModel
    {
        public string Url { get; set; } = string.Empty;

        public bool ScanSucceeded { get; set; }

        public string Message { get; set; } = string.Empty;

        public int? WebsiteScanId { get; set; }

        public bool WaveRequested { get; set; }

        public bool? WaveSucceeded { get; set; }
    }
}
