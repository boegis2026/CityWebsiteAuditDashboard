using CityWebsiteAuditDashboard.Models;

namespace CityWebsiteAuditDashboard.Services
{
    public interface IWebsiteScannerService
    {
        Task ScanAsync(WebsiteScan websiteScan);
    }
}