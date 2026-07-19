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

        // Stores one record for each authenticated Playwright auditing session.
        public DbSet<AuthenticatedAuditRun> AuthenticatedAuditRuns { get; set; }

        // Stores each separately rendered form/page state scanned during a run.
        public DbSet<AuthenticatedAuditStep> AuthenticatedAuditSteps { get; set; }

        // Stores rule-level violations and needs-review results for each
        // authenticated rendered-state scan.
        public DbSet<AuthenticatedAuditFinding> AuthenticatedAuditFindings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AuthenticatedAuditRun>(entity =>
            {
                // Audit history will commonly be displayed newest-first.
                entity.HasIndex(run => run.StartedAt);

                // Helps filter the dashboard by Running, Completed, or Failed.
                entity.HasIndex(run => run.Status);
            });

            modelBuilder.Entity<AuthenticatedAuditStep>(entity =>
            {
                // Each rendered step belongs to exactly one authenticated audit run.
                // Deleting a run also deletes its related step records so orphaned
                // audit steps are not left in the database.
                entity.HasOne(step => step.AuthenticatedAuditRun)
                    .WithMany(run => run.Steps)
                    .HasForeignKey(step => step.AuthenticatedAuditRunId)
                    .OnDelete(DeleteBehavior.Cascade);

                // A run should never contain two records with the same step number.
                entity.HasIndex(step => new
                {
                    step.AuthenticatedAuditRunId,
                    step.StepNumber
                })
                    .IsUnique();

                // Useful when displaying or querying steps by scan time.
                entity.HasIndex(step => step.ScannedAt);


            });

            modelBuilder.Entity<AuthenticatedAuditFinding>(entity =>
            {
                /*
                 * Each finding belongs to one scanned rendered state.
                 *
                 * Deleting an audit step also removes its rule-level findings so
                 * inaccessible orphan records cannot remain in the database.
                 */
                entity.HasOne(finding => finding.AuthenticatedAuditStep)
                    .WithMany(step => step.Findings)
                    .HasForeignKey(finding => finding.AuthenticatedAuditStepId)
                    .OnDelete(DeleteBehavior.Cascade);

                /*
                 * Axe reports each rule once within a result category for a given
                 * page state. This prevents the same Violation or NeedsReview rule
                 * from accidentally being saved twice for the same scanned step.
                 */
                entity.HasIndex(finding => new
                {
                    finding.AuthenticatedAuditStepId,
                    finding.FindingType,
                    finding.RuleId
                })
                    .IsUnique();
            });
        }
    }
}
