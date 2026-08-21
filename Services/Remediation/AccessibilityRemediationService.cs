using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Services.Remediation;

public sealed class AccessibilityRemediationService
{
    private readonly ApplicationDbContext _dbContext;

    public AccessibilityRemediationService(
        ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AccessibilityRemediationItem> CreateForFindingAsync(
        int authenticatedAuditFindingId,
        string? assignedTo = null,
        string? notes = null,
        string? changedBy = null)
    {
        AuthenticatedAuditFinding? finding =
            await _dbContext.AuthenticatedAuditFindings
                .FirstOrDefaultAsync(
                    finding => finding.Id == authenticatedAuditFindingId);

        if (finding is null)
        {
            throw new InvalidOperationException(
                "The authenticated audit finding could not be found.");
        }

        AccessibilityRemediationFindingOccurrence? existingOccurrence =
            await _dbContext.AccessibilityRemediationFindingOccurrences
                .Include(occurrence => occurrence.RemediationItem)
                .FirstOrDefaultAsync(
                    occurrence =>
                        occurrence.AuthenticatedAuditFindingId
                        == authenticatedAuditFindingId);

        if (existingOccurrence is not null)
        {
            return existingOccurrence.RemediationItem;
        }

        DateTime now = DateTime.UtcNow;

        AccessibilityRemediationItem remediationItem = new()
        {
            Status = AccessibilityRemediationStatus.Open,
            AssignedTo = string.IsNullOrWhiteSpace(assignedTo)
                ? null
                : assignedTo.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        remediationItem.FindingOccurrences.Add(
            new AccessibilityRemediationFindingOccurrence
            {
                AuthenticatedAuditFindingId =
                    authenticatedAuditFindingId,

                MatchMethod = "InitialFinding",

                MatchConfidence = 1.0000m,

                LinkedAt = now,

                LinkedBy = changedBy
            });

        remediationItem.History.Add(
            new AccessibilityRemediationHistory
            {
                EventType = "Created",

                PreviousStatus = null,

                NewStatus =
                    AccessibilityRemediationStatus.Open,

                PreviousAssignee = null,

                NewAssignee = remediationItem.AssignedTo,

                Notes = string.IsNullOrWhiteSpace(notes)
                    ? null
                    : notes.Trim(),

                ChangedAt = now,

                ChangedBy = changedBy
            });

        _dbContext.AccessibilityRemediationItems.Add(
            remediationItem);

        await _dbContext.SaveChangesAsync();

        return remediationItem;
    }

    public async Task UpdateAsync(
    int remediationItemId,
    AccessibilityRemediationStatus newStatus,
    string? assignedTo,
    string? notes,
    string? changedBy = null)
    {
        AccessibilityRemediationItem? item =
            await _dbContext.AccessibilityRemediationItems
                .FirstOrDefaultAsync(
                    item => item.Id == remediationItemId);

        if (item is null)
        {
            throw new InvalidOperationException(
                "The remediation item could not be found.");
        }

        if (newStatus == AccessibilityRemediationStatus.Verified)
        {
            throw new InvalidOperationException(
                "Verified status must be set through the verification workflow.");
        }

        string? cleanedAssignee =
            string.IsNullOrWhiteSpace(assignedTo)
                ? null
                : assignedTo.Trim();

        string? cleanedNotes =
            string.IsNullOrWhiteSpace(notes)
                ? null
                : notes.Trim();

        if (newStatus == AccessibilityRemediationStatus.WontFix &&
            string.IsNullOrWhiteSpace(cleanedNotes))
        {
            throw new InvalidOperationException(
                "A reason is required when marking an item as Won't Fix.");
        }

        AccessibilityRemediationStatus previousStatus =
            item.Status;

        string? previousAssignee =
            item.AssignedTo;

        bool statusChanged =
            previousStatus != newStatus;

        bool assigneeChanged =
            !string.Equals(
                previousAssignee,
                cleanedAssignee,
                StringComparison.Ordinal);

        bool noteAdded =
            !string.IsNullOrWhiteSpace(cleanedNotes);

        if (!statusChanged &&
            !assigneeChanged &&
            !noteAdded)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;

        item.Status = newStatus;
        item.AssignedTo = cleanedAssignee;
        item.UpdatedAt = now;

        item.History.Add(
            new AccessibilityRemediationHistory
            {
                EventType =
                    statusChanged
                        ? "StatusChanged"
                        : assigneeChanged
                            ? "AssignmentChanged"
                            : "NoteAdded",

                PreviousStatus = previousStatus,
                NewStatus = newStatus,

                PreviousAssignee = previousAssignee,
                NewAssignee = cleanedAssignee,

                Notes = cleanedNotes,

                ChangedAt = now,
                ChangedBy = changedBy
            });

        await _dbContext.SaveChangesAsync();
    }
}
