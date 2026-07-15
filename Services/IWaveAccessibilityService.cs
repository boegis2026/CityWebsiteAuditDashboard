namespace CityWebsiteAuditDashboard.Services
{
    public interface IWaveAccessibilityService
    {
        Task<WaveAccessibilityResult> ScanAsync(
            string websiteUrl,
            CancellationToken cancellationToken = default);
    }
}
