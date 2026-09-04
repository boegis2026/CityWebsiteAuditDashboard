using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Services.Remediation;

public sealed class AccessibilityRemediationService
{
    private const int MaximumAssigneeLength = 200;
    private const int MaximumNotesLength = 4000;
    private const int MaximumChangedByLength = 200;

    private readonly ApplicationDbContext _dbContext;

    public AccessibilityRemediationService(
        ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Starts durable remediation tracking for one immutable
    /// authenticated accessibility finding.
    ///
    /// If the finding is already being tracked, the existing
    /// remediation item is returned instead of creating a duplicate.
    /// </summary>
    public async Task<AccessibilityRemediationItem>
        CreateForFindingAsync(
            int authenticatedAuditFindingId,
            string? assignedTo = null,
            string? notes = null,
            string? changedBy = null,
            CancellationToken cancellationToken = default)
    {
        if (authenticatedAuditFindingId <= 0)
        {
            throw new InvalidOperationException(
                "A valid authenticated accessibility finding is required.");
        }

        bool findingExists =
            await _dbContext.AuthenticatedAuditFindings
                .AsNoTracking()
                .AnyAsync(
                    finding =>
                        finding.Id ==
                        authenticatedAuditFindingId,
                    cancellationToken);

        if (!findingExists)
        {
            throw new InvalidOperationException(
                "The authenticated audit finding could not be found.");
        }

        /*
         * One immutable authenticated finding can belong to only one
         * durable remediation item.
         */
        AccessibilityRemediationFindingOccurrence?
            existingOccurrence =
                await _dbContext
                    .AccessibilityRemediationFindingOccurrences
                    .AsNoTracking()
                    .Include(occurrence =>
                        occurrence.RemediationItem)
                    .FirstOrDefaultAsync(
                        occurrence =>
                            occurrence.AuthenticatedAuditFindingId ==
                            authenticatedAuditFindingId,
                        cancellationToken);

        if (existingOccurrence is not null)
        {
            return existingOccurrence.RemediationItem;
        }

        string? cleanedAssignee =
            CleanOptionalText(
                assignedTo,
                MaximumAssigneeLength);

        string? cleanedNotes =
            CleanOptionalText(
                notes,
                MaximumNotesLength);

        string? cleanedChangedBy =
            CleanOptionalText(
                changedBy,
                MaximumChangedByLength);

        DateTime now =
            DateTime.UtcNow;

        AccessibilityRemediationItem remediationItem =
            new()
            {
                Status =
                    AccessibilityRemediationStatus.Open,

                AssignedTo =
                    cleanedAssignee,

                CreatedAt =
                    now,

                UpdatedAt =
                    now
            };

        remediationItem.FindingOccurrences.Add(
            new AccessibilityRemediationFindingOccurrence
            {
                AuthenticatedAuditFindingId =
                    authenticatedAuditFindingId,

                MatchMethod =
                    "InitialFinding",

                MatchConfidence =
                    1.0000m,

                LinkedAt =
                    now,

                LinkedBy =
                    cleanedChangedBy
            });

        remediationItem.History.Add(
            new AccessibilityRemediationHistory
            {
                EventType =
                    "Created",

                PreviousStatus =
                    null,

                NewStatus =
                    AccessibilityRemediationStatus.Open,

                PreviousAssignee =
                    null,

                NewAssignee =
                    cleanedAssignee,

                Notes =
                    cleanedNotes,

                ChangedAt =
                    now,

                ChangedBy =
                    cleanedChangedBy
            });

        _dbContext.AccessibilityRemediationItems.Add(
            remediationItem);

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return remediationItem;
        }
        catch (DbUpdateException)
        {
            /*
             * The database has a unique index on
             * AuthenticatedAuditFindingId.
             *
             * If two users start remediation for the same finding at
             * almost the same time, one insert can lose that race.
             *
             * Clear the failed tracked insert and check whether the
             * other request successfully created the durable item.
             */
            _dbContext.ChangeTracker.Clear();

            AccessibilityRemediationFindingOccurrence?
                concurrentlyCreatedOccurrence =
                    await _dbContext
                        .AccessibilityRemediationFindingOccurrences
                        .AsNoTracking()
                        .Include(occurrence =>
                            occurrence.RemediationItem)
                        .FirstOrDefaultAsync(
                            occurrence =>
                                occurrence
                                    .AuthenticatedAuditFindingId ==
                                authenticatedAuditFindingId,
                            cancellationToken);

            if (concurrentlyCreatedOccurrence is not null)
            {
                return concurrentlyCreatedOccurrence.RemediationItem;
            }

            throw;
        }
    }

    /// <summary>
    /// Updates normal remediation workflow state, assignment, or notes.
    ///
    /// Verified may only be entered through the verification workflow.
    /// A Verified item may only be reopened by later retest evidence
    /// detecting the issue again.
    /// </summary>
    public async Task UpdateAsync(
        int remediationItemId,
        AccessibilityRemediationStatus newStatus,
        string? assignedTo,
        string? notes,
        string? changedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (remediationItemId <= 0)
        {
            throw new InvalidOperationException(
                "A valid remediation item is required.");
        }

        if (!Enum.IsDefined(
            typeof(AccessibilityRemediationStatus),
            newStatus))
        {
            throw new InvalidOperationException(
                "The selected remediation status is not valid.");
        }

        AccessibilityRemediationItem? item =
            await _dbContext.AccessibilityRemediationItems
                .FirstOrDefaultAsync(
                    item =>
                        item.Id ==
                        remediationItemId,
                    cancellationToken);

        if (item is null)
        {
            throw new InvalidOperationException(
                "The remediation item could not be found.");
        }

        /*
         * Verified is a protected lifecycle state.
         *
         * Entering Verified requires explicit verification evidence.
         */
        if (newStatus ==
                AccessibilityRemediationStatus.Verified &&
            item.Status !=
                AccessibilityRemediationStatus.Verified)
        {
            throw new InvalidOperationException(
                "Verified status must be set through the verification workflow.");
        }

        /*
         * A Verified item should not be manually reopened through the
         * ordinary status dropdown.
         *
         * Later retest evidence detecting the issue again performs the
         * reopen and records the Reopened history event.
         */
        if (item.Status ==
                AccessibilityRemediationStatus.Verified &&
            newStatus !=
                AccessibilityRemediationStatus.Verified)
        {
            throw new InvalidOperationException(
                "A Verified remediation item cannot be manually reopened. " +
                "Run a new retest; if the issue is detected again, the " +
                "remediation will automatically reopen to In Progress.");
        }

        string? cleanedAssignee =
            CleanOptionalText(
                assignedTo,
                MaximumAssigneeLength);

        string? cleanedNotes =
            CleanOptionalText(
                notes,
                MaximumNotesLength);

        string? cleanedChangedBy =
            CleanOptionalText(
                changedBy,
                MaximumChangedByLength);

        /*
         * A reason is required only when ENTERING Won't Fix.
         * Existing Won't Fix items can still have assignment changes
         * without repeatedly supplying the original reason.
         */
        if (item.Status !=
                AccessibilityRemediationStatus.WontFix &&
            newStatus ==
                AccessibilityRemediationStatus.WontFix &&
            string.IsNullOrWhiteSpace(
                cleanedNotes))
        {
            throw new InvalidOperationException(
                "A reason is required when marking an item as Won't Fix.");
        }

        AccessibilityRemediationStatus previousStatus =
            item.Status;

        string? previousAssignee =
            item.AssignedTo;

        bool statusChanged =
            previousStatus !=
            newStatus;

        bool assigneeChanged =
            !string.Equals(
                previousAssignee,
                cleanedAssignee,
                StringComparison.Ordinal);

        bool noteAdded =
            !string.IsNullOrWhiteSpace(
                cleanedNotes);

        if (!statusChanged &&
            !assigneeChanged &&
            !noteAdded)
        {
            return;
        }

        DateTime now =
            DateTime.UtcNow;

        item.Status =
            newStatus;

        item.AssignedTo =
            cleanedAssignee;

        item.UpdatedAt =
            now;

        string eventType =
            statusChanged
                ? "StatusChanged"
                : assigneeChanged
                    ? "AssignmentChanged"
                    : "NoteAdded";

        item.History.Add(
            new AccessibilityRemediationHistory
            {
                EventType =
                    eventType,

                PreviousStatus =
                    previousStatus,

                NewStatus =
                    newStatus,

                PreviousAssignee =
                    previousAssignee,

                NewAssignee =
                    cleanedAssignee,

                Notes =
                    cleanedNotes,

                ChangedAt =
                    now,

                ChangedBy =
                    cleanedChangedBy
            });

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(
                "The remediation item changed while your update was " +
                "being saved. Reload the item and try again.");
        }
    }

    private static string? CleanOptionalText(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(
            value))
        {
            return null;
        }

        string cleaned =
            value.Trim();

        return cleaned.Length <=
            maximumLength
                ? cleaned
                : cleaned[..maximumLength];
    }
}
