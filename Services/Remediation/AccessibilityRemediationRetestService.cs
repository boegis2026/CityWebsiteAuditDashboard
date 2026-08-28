using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Services.Remediation;

public sealed class AccessibilityRemediationRetestService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly AccessibilityRemediationMatcher _matcher;

    public AccessibilityRemediationRetestService(
        ApplicationDbContext dbContext,
        AccessibilityRemediationMatcher matcher)
    {
        _dbContext = dbContext;
        _matcher = matcher;
    }

    public async Task<AccessibilityRemediationRetest> RecordRetestAsync(
        int remediationItemId,
        int authenticatedAuditStepId,
        string? notes = null,
        string? retestedBy = null,
        CancellationToken cancellationToken = default)
    {
        AccessibilityRemediationItem? item =
            await _dbContext.AccessibilityRemediationItems
                .FirstOrDefaultAsync(
                    item => item.Id == remediationItemId,
                    cancellationToken);

        if (item is null)
        {
            throw new InvalidOperationException(
                "The remediation item could not be found.");
        }

        AccessibilityRemediationMatchResult match =
            await _matcher.MatchAsync(
                remediationItemId,
                authenticatedAuditStepId,
                cancellationToken);

        string? cleanedNotes =
            string.IsNullOrWhiteSpace(notes)
                ? null
                : notes.Trim();

        if (cleanedNotes?.Length > 4000)
        {
            cleanedNotes = cleanedNotes[..4000];
        }

        string? cleanedRetestedBy =
            string.IsNullOrWhiteSpace(retestedBy)
                ? null
                : retestedBy.Trim();

        if (cleanedRetestedBy?.Length > 200)
        {
            cleanedRetestedBy =
                cleanedRetestedBy[..200];
        }

        DateTime now = DateTime.UtcNow;

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        /*
         * If the same finding was detected again, link that new scan
         * occurrence to the durable remediation item.
         */
        if (match.Result ==
                AccessibilityRemediationRetestResult.Detected &&
            match.MatchedAuthenticatedAuditFindingId.HasValue)
        {
            int matchedFindingId =
                match.MatchedAuthenticatedAuditFindingId.Value;

            AccessibilityRemediationFindingOccurrence?
                existingOccurrence =
                    await _dbContext
                        .AccessibilityRemediationFindingOccurrences
                        .FirstOrDefaultAsync(
                            occurrence =>
                                occurrence.AuthenticatedAuditFindingId ==
                                matchedFindingId,
                            cancellationToken);

            if (existingOccurrence is null)
            {
                _dbContext
                    .AccessibilityRemediationFindingOccurrences
                    .Add(
                        new AccessibilityRemediationFindingOccurrence
                        {
                            AccessibilityRemediationItemId =
                                remediationItemId,

                            AuthenticatedAuditFindingId =
                                matchedFindingId,

                            MatchMethod =
                                match.MatchMethod,

                            MatchConfidence =
                                match.MatchConfidence,

                            LinkedAt =
                                now,

                            LinkedBy =
                                cleanedRetestedBy
                        });
            }
            else if (
                existingOccurrence.AccessibilityRemediationItemId !=
                remediationItemId)
            {
                throw new InvalidOperationException(
                    "The matched accessibility finding is already linked " +
                    "to another remediation item.");
            }
        }

        AccessibilityRemediationRetest retest = new()
        {
            AccessibilityRemediationItemId =
                remediationItemId,

            AuthenticatedAuditStepId =
                authenticatedAuditStepId,

            MatchedAuthenticatedAuditFindingId =
                match.MatchedAuthenticatedAuditFindingId,

            Result =
                match.Result,

            MatchMethod =
                match.MatchMethod,

            MatchConfidence =
                match.MatchConfidence,

            RetestedAt =
                now,

            Notes =
                cleanedNotes,

            RetestedBy =
                cleanedRetestedBy
        };

        _dbContext.AccessibilityRemediationRetests.Add(
            retest);

        AccessibilityRemediationStatus previousStatus =
            item.Status;

        string eventType;

        /*
         * A failed retest means a claimed fix is not ready for
         * verification anymore.
         */
        if (match.Result ==
                AccessibilityRemediationRetestResult.Detected &&
            (item.Status ==
                AccessibilityRemediationStatus.Fixed ||
             item.Status ==
                AccessibilityRemediationStatus.Verified))
        {
            item.Status =
                AccessibilityRemediationStatus.InProgress;

            eventType = "Reopened";
        }
        else
        {
            eventType =
                match.Result switch
                {
                    AccessibilityRemediationRetestResult.Detected =>
                        "RetestDetected",

                    AccessibilityRemediationRetestResult.NotDetected =>
                        "RetestPassed",

                    AccessibilityRemediationRetestResult.Inconclusive =>
                        "RetestInconclusive",

                    AccessibilityRemediationRetestResult.Failed =>
                        "RetestFailed",

                    _ =>
                        "Retested"
                };
        }

        item.UpdatedAt = now;

        item.History.Add(
            new AccessibilityRemediationHistory
            {
                EventType =
                    eventType,

                PreviousStatus =
                    previousStatus,

                NewStatus =
                    item.Status,

                PreviousAssignee =
                    item.AssignedTo,

                NewAssignee =
                    item.AssignedTo,

                Notes =
                    BuildHistoryNotes(
                        match,
                        cleanedNotes),

                ChangedAt =
                    now,

                ChangedBy =
                    cleanedRetestedBy
            });

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return retest;
    }

    private static string BuildHistoryNotes(
        AccessibilityRemediationMatchResult match,
        string? userNotes)
    {
        List<string> parts = new()
        {
            $"Retest result: {match.Result}.",
            $"Match method: {match.MatchMethod}.",
            $"Match confidence: {match.MatchConfidence:P0}."
        };

        if (!string.IsNullOrWhiteSpace(match.Message))
        {
            parts.Add(match.Message);
        }

        if (!string.IsNullOrWhiteSpace(userNotes))
        {
            parts.Add($"Notes: {userNotes}");
        }

        string combined =
            string.Join(
                " ",
                parts);

        return combined.Length <= 4000
            ? combined
            : combined[..4000];
    }

    public async Task VerifyAsync(
    int remediationItemId,
    string? notes = null,
    string? verifiedBy = null,
    CancellationToken cancellationToken = default)
    {
        AccessibilityRemediationItem? item =
            await _dbContext.AccessibilityRemediationItems
                .Include(item => item.Retests)
                .FirstOrDefaultAsync(
                    item => item.Id == remediationItemId,
                    cancellationToken);

        if (item is null)
        {
            throw new InvalidOperationException(
                "The remediation item could not be found.");
        }

        if (item.Status != AccessibilityRemediationStatus.Fixed)
        {
            throw new InvalidOperationException(
                "Only an item marked Fixed – Awaiting Verification can be verified.");
        }

        AccessibilityRemediationRetest? latestRetest =
            item.Retests
                .OrderByDescending(retest => retest.RetestedAt)
                .ThenByDescending(retest => retest.Id)
                .FirstOrDefault();

        if (latestRetest is null)
        {
            throw new InvalidOperationException(
                "This remediation item must be successfully retested before it can be verified.");
        }

        if (latestRetest.Result !=
            AccessibilityRemediationRetestResult.NotDetected)
        {
            throw new InvalidOperationException(
                "The latest retest did not confirm that the issue is no longer detected.");
        }

        string? cleanedNotes =
            string.IsNullOrWhiteSpace(notes)
                ? null
                : notes.Trim();

        if (cleanedNotes?.Length > 4000)
        {
            cleanedNotes = cleanedNotes[..4000];
        }

        string? cleanedVerifiedBy =
            string.IsNullOrWhiteSpace(verifiedBy)
                ? null
                : verifiedBy.Trim();

        if (cleanedVerifiedBy?.Length > 200)
        {
            cleanedVerifiedBy =
                cleanedVerifiedBy[..200];
        }

        DateTime now = DateTime.UtcNow;

        AccessibilityRemediationStatus previousStatus =
            item.Status;

        item.Status =
            AccessibilityRemediationStatus.Verified;

        item.UpdatedAt =
            now;

        item.History.Add(
            new AccessibilityRemediationHistory
            {
                EventType =
                    "Verified",

                PreviousStatus =
                    previousStatus,

                NewStatus =
                    AccessibilityRemediationStatus.Verified,

                PreviousAssignee =
                    item.AssignedTo,

                NewAssignee =
                    item.AssignedTo,

                Notes =
                    cleanedNotes ??
                    "Verified after the latest retest no longer detected the tracked accessibility issue.",

                ChangedAt =
                    now,

                ChangedBy =
                    cleanedVerifiedBy
            });

        await _dbContext.SaveChangesAsync(
            cancellationToken);
    }
}
