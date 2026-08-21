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

        public DbSet<AuthenticatedAuditFindingNode> AuthenticatedAuditFindingNodes { get; set; }

        public DbSet<AccessibilityRemediationItem> AccessibilityRemediationItems { get; set; }

        public DbSet<AccessibilityRemediationHistory> AccessibilityRemediationHistories { get; set; }

        public DbSet<AccessibilityRemediationFindingOccurrence> AccessibilityRemediationFindingOccurrences { get; set; }

        public DbSet<AccessibilityRemediationRetest>  AccessibilityRemediationRetests { get; set; }

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

            modelBuilder.Entity<AuthenticatedAuditFindingNode>(entity =>
            {
                entity.Property(node => node.Target)
                    .HasMaxLength(2000);

                entity.Property(node => node.Html)
                    .HasMaxLength(10000);

                entity.Property(node => node.FailureSummary)
                    .HasMaxLength(4000);

                /*
                 * A finding may affect multiple page elements. Deleting the finding
                 * should also delete its saved affected-element records.
                 */
                entity.HasOne(node => node.AuthenticatedAuditFinding)
                    .WithMany(finding => finding.Nodes)
                    .HasForeignKey(node => node.AuthenticatedAuditFindingId)
                    .OnDelete(DeleteBehavior.Cascade);
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

            modelBuilder.Entity<AccessibilityRemediationItem>(entity =>
            {
                entity.Property(item => item.Status)
                    .HasConversion<string>()
                    .HasMaxLength(30);

                entity.HasIndex(item => item.Status);

                entity.HasIndex(item => item.AssignedTo);
            });

            modelBuilder.Entity<AccessibilityRemediationHistory>(entity =>
            {
                entity.Property(history => history.PreviousStatus)
                    .HasConversion<string>()
                    .HasMaxLength(30);

                entity.Property(history => history.NewStatus)
                    .HasConversion<string>()
                    .HasMaxLength(30);

                entity.HasOne(history => history.RemediationItem)
                    .WithMany(item => item.History)
                    .HasForeignKey(history => history.AccessibilityRemediationItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(history => history.AccessibilityRemediationItemId);

                entity.HasIndex(history => history.ChangedAt);
            });

            modelBuilder.Entity<AccessibilityRemediationFindingOccurrence>(entity =>
            {
                entity.Property(occurrence => occurrence.MatchConfidence)
                    .HasPrecision(5, 4);

                entity.HasOne(occurrence => occurrence.RemediationItem)
                    .WithMany(item => item.FindingOccurrences)
                    .HasForeignKey(occurrence => occurrence.AccessibilityRemediationItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(occurrence => occurrence.AuthenticatedAuditFinding)
                    .WithMany()
                    .HasForeignKey(occurrence => occurrence.AuthenticatedAuditFindingId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(occurrence => occurrence.AuthenticatedAuditFindingId)
                    .IsUnique();

                entity.HasIndex(occurrence => occurrence.AccessibilityRemediationItemId);
            });

            modelBuilder.Entity<AccessibilityRemediationRetest>(entity =>
            {
                entity.Property(retest => retest.Result)
                    .HasConversion<string>()
                    .HasMaxLength(30);

                entity.Property(retest => retest.MatchConfidence)
                    .HasPrecision(5, 4);

                entity.HasOne(retest => retest.RemediationItem)
                    .WithMany(item => item.Retests)
                    .HasForeignKey(retest =>
                        retest.AccessibilityRemediationItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                /*
                 * Retests are historical evidence.
                 * Do not allow an authenticated audit step to be deleted while
                 * a remediation retest references it.
                 */
                entity.HasOne(retest => retest.AuthenticatedAuditStep)
                    .WithMany()
                    .HasForeignKey(retest =>
                        retest.AuthenticatedAuditStepId)
                    .OnDelete(DeleteBehavior.Restrict);

                /*
                 * Likewise, preserve the matched finding used as evidence for
                 * the retest result.
                 */
                entity.HasOne(retest =>
                        retest.MatchedAuthenticatedAuditFinding)
                    .WithMany()
                    .HasForeignKey(retest =>
                        retest.MatchedAuthenticatedAuditFindingId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(retest =>
                    retest.AccessibilityRemediationItemId);

                entity.HasIndex(retest =>
                    retest.RetestedAt);

                entity.HasIndex(retest =>
                    retest.Result);
            });
        }
    }
}
