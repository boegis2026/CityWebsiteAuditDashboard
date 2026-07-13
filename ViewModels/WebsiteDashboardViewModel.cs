using CityWebsiteAuditDashboard.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CityWebsiteAuditDashboard.ViewModels
{
    public class WebsiteDashboardViewModel
    {
        public List<WebsiteScan> Scans { get; set; } = [];

        public string? SearchString { get; set; }

        public string? ServerFilter { get; set; }

        public string SortBy { get; set; } = "date";

        public bool SortDescending { get; set; } = true;

        public List<SelectListItem> ServerOptions { get; set; } = [];

        public int TotalScans { get; set; }

        public int SuccessfulScans { get; set; }

        public int FailedScans { get; set; }

        public int CurrentPage { get; set; }

        public int TotalPages { get; set; }

        public int PageSize { get; set; }

        public long AverageResponseTime { get; set; }
    }
}