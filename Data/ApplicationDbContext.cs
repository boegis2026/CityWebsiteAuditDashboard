using CityWebsiteAuditDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<WebsiteScan> WebsiteScans { get; set; }

        public DbSet<WaveAccessibilityIssue> WaveAccessibilityIssues { get; set; }
    }
}
