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

        public DbSet<WaveAccessibilityIssue>
            WaveAccessibilityIssues
        { get; set; }

        /*
         * Stores one record for each authenticated
         * Playwright auditing session.
         */
        public DbSet<AuthenticatedAuditRun>
            AuthenticatedAuditRuns
        { get; set; }

        /*
         * Stores each separately rendered form/page state
         * scanned during an authenticated audit run.
         */
        public DbSet<AuthenticatedAuditStep>
            AuthenticatedAuditSteps
        { get; set; }

        /*
         * Stores rule-level violations and needs-review
         * results for each authenticated rendered-state scan.
         */
        public DbSet<AuthenticatedAuditFinding>
            AuthenticatedAuditFindings
        { get; set; }

        public DbSet<AuthenticatedAuditFindingNode>
            AuthenticatedAuditFindingNodes
        { get; set; }

        public DbSet<AccessibilityRemediationItem>
            AccessibilityRemediationItems
        { get; set; }

        public DbSet<AccessibilityRemediationHistory>
            AccessibilityRemediationHistories
        { get; set; }

        public DbSet<AccessibilityRemediationFindingOccurrence>
            AccessibilityRemediationFindingOccurrences
        { get; set; }

        public DbSet<AccessibilityRemediationRetest>
            AccessibilityRemediationRetests
        { get; set; }


        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(
                modelBuilder);


            modelBuilder.Entity<AuthenticatedAuditRun>(
                entity =>
                {
                    /*
                     * Audit history is commonly displayed
                     * newest-first.
                     */
                    entity.HasIndex(run =>
                        run.StartedAt);

                    /*
                     * Helps filter by Running, Completed,
                     * Interrupted, or Failed.
                     */
                    entity.HasIndex(run =>
                        run.Status);
                });


            modelBuilder.Entity<AuthenticatedAuditStep>(
                entity =>
                {
                    /*
                     * Each rendered state belongs to exactly one
                     * authenticated audit run.
                     *
                     * Deleting a run normally deletes its child
                     * steps, unless remediation evidence references
                     * those steps/findings through a Restrict FK.
                     */
                    entity.HasOne(step =>
                            step.AuthenticatedAuditRun)
                        .WithMany(run =>
                            run.Steps)
                        .HasForeignKey(step =>
                            step.AuthenticatedAuditRunId)
                        .OnDelete(
                            DeleteBehavior.Cascade);

                    /*
                     * A run must not contain duplicate step numbers.
                     */
                    entity.HasIndex(step =>
                            new
                            {
                                step.AuthenticatedAuditRunId,
                                step.StepNumber
                            })
                        .IsUnique();

                    entity.HasIndex(step =>
                        step.ScannedAt);
                });


            modelBuilder.Entity<AuthenticatedAuditFindingNode>(
                entity =>
                {
                    entity.Property(node =>
                            node.Target)
                        .HasMaxLength(
                            2000);

                    entity.Property(node =>
                            node.Html)
                        .HasMaxLength(
                            10000);

                    entity.Property(node =>
                            node.FailureSummary)
                        .HasMaxLength(
                            4000);

                    /*
                     * A finding may affect multiple elements.
                     * Deleting the finding removes its nodes.
                     */
                    entity.HasOne(node =>
                            node.AuthenticatedAuditFinding)
                        .WithMany(finding =>
                            finding.Nodes)
                        .HasForeignKey(node =>
                            node.AuthenticatedAuditFindingId)
                        .OnDelete(
                            DeleteBehavior.Cascade);
                });


            modelBuilder.Entity<AuthenticatedAuditFinding>(
                entity =>
                {
                    /*
                     * Each finding belongs to one rendered
                     * authenticated audit state.
                     */
                    entity.HasOne(finding =>
                            finding.AuthenticatedAuditStep)
                        .WithMany(step =>
                            step.Findings)
                        .HasForeignKey(finding =>
                            finding.AuthenticatedAuditStepId)
                        .OnDelete(
                            DeleteBehavior.Cascade);

                    /*
                     * axe-core reports each rule once within a result
                     * category for a rendered state.
                     *
                     * Prevent duplicate Violation / NeedsReview rows.
                     */
                    entity.HasIndex(finding =>
                            new
                            {
                                finding.AuthenticatedAuditStepId,
                                finding.FindingType,
                                finding.RuleId
                            })
                        .IsUnique();
                });


            modelBuilder.Entity<AccessibilityRemediationItem>(
                entity =>
                {
                    entity.Property(item =>
                            item.Status)
                        .HasConversion<string>()
                        .HasMaxLength(
                            30);

                    entity.HasIndex(item =>
                        item.Status);

                    entity.HasIndex(item =>
                        item.AssignedTo);
                });


            modelBuilder.Entity<AccessibilityRemediationHistory>(
                entity =>
                {
                    entity.Property(history =>
                            history.PreviousStatus)
                        .HasConversion<string>()
                        .HasMaxLength(
                            30);

                    entity.Property(history =>
                            history.NewStatus)
                        .HasConversion<string>()
                        .HasMaxLength(
                            30);

                    /*
                     * Remediation history belongs to the durable
                     * remediation item itself.
                     *
                     * If the remediation item is explicitly deleted,
                     * its history can be removed with it.
                     */
                    entity.HasOne(history =>
                            history.RemediationItem)
                        .WithMany(item =>
                            item.History)
                        .HasForeignKey(history =>
                            history.AccessibilityRemediationItemId)
                        .OnDelete(
                            DeleteBehavior.Cascade);

                    entity.HasIndex(history =>
                        history.AccessibilityRemediationItemId);

                    entity.HasIndex(history =>
                        history.ChangedAt);
                });


            modelBuilder.Entity<
                AccessibilityRemediationFindingOccurrence>(
                entity =>
                {
                    entity.Property(occurrence =>
                            occurrence.MatchConfidence)
                        .HasPrecision(
                            5,
                            4);

                    /*
                     * Occurrences are part of the durable
                     * remediation item.
                     */
                    entity.HasOne(occurrence =>
                            occurrence.RemediationItem)
                        .WithMany(item =>
                            item.FindingOccurrences)
                        .HasForeignKey(occurrence =>
                            occurrence.AccessibilityRemediationItemId)
                        .OnDelete(
                            DeleteBehavior.Cascade);

                    /*
                     * IMPORTANT:
                     *
                     * An occurrence links durable remediation work to
                     * immutable authenticated audit evidence.
                     *
                     * Do NOT cascade-delete the occurrence when its
                     * source finding is deleted.
                     *
                     * Instead, prevent deletion of the source audit
                     * evidence while remediation still references it.
                     */
                    entity.HasOne(occurrence =>
                            occurrence.AuthenticatedAuditFinding)
                        .WithMany()
                        .HasForeignKey(occurrence =>
                            occurrence.AuthenticatedAuditFindingId)
                        .OnDelete(
                            DeleteBehavior.Restrict);

                    /*
                     * One immutable finding can belong to only one
                     * durable remediation item.
                     */
                    entity.HasIndex(occurrence =>
                            occurrence.AuthenticatedAuditFindingId)
                        .IsUnique();

                    entity.HasIndex(occurrence =>
                        occurrence.AccessibilityRemediationItemId);
                });


            modelBuilder.Entity<AccessibilityRemediationRetest>(
                entity =>
                {
                    entity.Property(retest =>
                            retest.Result)
                        .HasConversion<string>()
                        .HasMaxLength(
                            30);

                    entity.Property(retest =>
                            retest.MatchConfidence)
                        .HasPrecision(
                            5,
                            4);

                    entity.Property(retest =>
                            retest.RetestType)
                        .HasMaxLength(
                            50);

                    /*
                     * Retests belong to the durable remediation item.
                     */
                    entity.HasOne(retest =>
                            retest.RemediationItem)
                        .WithMany(item =>
                            item.Retests)
                        .HasForeignKey(retest =>
                            retest.AccessibilityRemediationItemId)
                        .OnDelete(
                            DeleteBehavior.Cascade);

                    /*
                     * Retests are historical evidence.
                     *
                     * Do not allow the authenticated state used by a
                     * retest to be deleted while the retest references it.
                     */
                    entity.HasOne(retest =>
                            retest.AuthenticatedAuditStep)
                        .WithMany()
                        .HasForeignKey(retest =>
                            retest.AuthenticatedAuditStepId)
                        .OnDelete(
                            DeleteBehavior.Restrict);

                    /*
                     * Preserve the matched finding used as evidence
                     * when a rule was detected again.
                     */
                    entity.HasOne(retest =>
                            retest.MatchedAuthenticatedAuditFinding)
                        .WithMany()
                        .HasForeignKey(retest =>
                            retest.MatchedAuthenticatedAuditFindingId)
                        .OnDelete(
                            DeleteBehavior.Restrict);

                    /*
                     * Preserve the exact original finding being retested.
                     */
                    entity.HasOne(retest =>
                            retest.OriginalAuthenticatedAuditFinding)
                        .WithMany()
                        .HasForeignKey(retest =>
                            retest.OriginalAuthenticatedAuditFindingId)
                        .OnDelete(
                            DeleteBehavior.Restrict);

                    /*
                     * Preserve the authenticated audit run that supplied
                     * formal/current-state retest evidence.
                     */
                    entity.HasOne(retest =>
                            retest.AuthenticatedAuditRun)
                        .WithMany()
                        .HasForeignKey(retest =>
                            retest.AuthenticatedAuditRunId)
                        .OnDelete(
                            DeleteBehavior.Restrict);

                    entity.HasIndex(retest =>
                        retest.AccessibilityRemediationItemId);

                    entity.HasIndex(retest =>
                        retest.RetestedAt);

                    entity.HasIndex(retest =>
                        retest.Result);

                    entity.HasIndex(retest =>
                        retest.AuthenticatedAuditRunId);

                    entity.HasIndex(retest =>
                        retest.OriginalAuthenticatedAuditFindingId);
                });
        }
    }
}
