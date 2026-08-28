using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Services.Remediation;

public sealed class AccessibilityRemediationRetestService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly AccessibilityRemediationMatcher _matcher;
    private readonly AccessibilityRemediationWorkflowComparisonService
    _workflowComparisonService;

    public AccessibilityRemediationRetestService(
        ApplicationDbContext dbContext,
        AccessibilityRemediationMatcher matcher,
        AccessibilityRemediationWorkflowComparisonService
        workflowComparisonService)
    {
        _dbContext = dbContext;
        _matcher = matcher;
        _workflowComparisonService = workflowComparisonService;
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

        int? authenticatedAuditRunId =
            await _dbContext.AuthenticatedAuditSteps
                .AsNoTracking()
                .Where(step =>
                    step.Id == authenticatedAuditStepId)
                .Select(step =>
                    (int?)step.AuthenticatedAuditRunId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!authenticatedAuditRunId.HasValue)
        {
            throw new InvalidOperationException(
                "The authenticated audit step used for the retest could not be found.");
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

            AuthenticatedAuditRunId =
                authenticatedAuditRunId.Value,

            AuthenticatedAuditStepId =
                authenticatedAuditStepId,

            MatchedAuthenticatedAuditFindingId =
                match.MatchedAuthenticatedAuditFindingId,

            Result =
                match.Result,

            RetestType =
                "CurrentState",

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

    public async Task<AccessibilityWorkflowRetestApplyResult>
    ApplyWorkflowRetestAsync(
        int originalAuditRunId,
        int retestAuditRunId,
        string? notes = null,
        string? retestedBy = null,
        CancellationToken cancellationToken = default)
    {
        AccessibilityRemediationWorkflowComparisonResult comparison =
            await _workflowComparisonService.CompareAsync(
                originalAuditRunId,
                retestAuditRunId,
                cancellationToken);

        if (comparison.TotalTracked == 0)
        {
            throw new InvalidOperationException(
                "The original audit does not have any tracked remediation items.");
        }

        string? cleanedNotes =
            string.IsNullOrWhiteSpace(notes)
                ? null
                : notes.Trim();

        if (cleanedNotes?.Length > 4000)
        {
            cleanedNotes =
                cleanedNotes[..4000];
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

        List<int> remediationItemIds =
            comparison.Items
                .Select(item =>
                    item.RemediationItemId)
                .Distinct()
                .ToList();

        bool alreadyApplied =
            await _dbContext.AccessibilityRemediationRetests
                .AsNoTracking()
                .AnyAsync(
                    retest =>
                        retest.AuthenticatedAuditRunId ==
                            retestAuditRunId &&
                        remediationItemIds.Contains(
                            retest.AccessibilityRemediationItemId),
                    cancellationToken);

        if (alreadyApplied)
        {
            throw new InvalidOperationException(
                "This authenticated audit run has already been used " +
                "to formally retest one or more of these remediation items.");
        }

        Dictionary<int, AccessibilityRemediationItem>
            remediationItems =
                await _dbContext.AccessibilityRemediationItems
                    .Where(item =>
                        remediationItemIds.Contains(item.Id))
                    .ToDictionaryAsync(
                        item => item.Id,
                        cancellationToken);

        if (remediationItems.Count !=
            remediationItemIds.Count)
        {
            throw new InvalidOperationException(
                "One or more remediation items could not be loaded.");
        }

        DateTime now =
            DateTime.UtcNow;

        int reopenedCount =
            0;

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        foreach (
            AccessibilityRemediationWorkflowComparisonItem comparisonItem
            in comparison.Items)
        {
            if (!remediationItems.TryGetValue(
                comparisonItem.RemediationItemId,
                out AccessibilityRemediationItem? remediationItem))
            {
                throw new InvalidOperationException(
                    "A remediation item from the workflow comparison " +
                    "could not be loaded.");
            }

            /*
             * If the same finding is still present in the new workflow audit,
             * link the new immutable finding occurrence to the existing
             * durable remediation item.
             */
            if (comparisonItem.Result ==
                    AccessibilityRemediationRetestResult.Detected &&
                comparisonItem
                    .MatchedAuthenticatedAuditFindingId
                    .HasValue)
            {
                int matchedFindingId =
                    comparisonItem
                        .MatchedAuthenticatedAuditFindingId
                        .Value;

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
                                    remediationItem.Id,

                                AuthenticatedAuditFindingId =
                                    matchedFindingId,

                                MatchMethod =
                                    comparisonItem.MatchMethod,

                                MatchConfidence =
                                    comparisonItem.MatchConfidence,

                                LinkedAt =
                                    now,

                                LinkedBy =
                                    cleanedRetestedBy
                            });
                }
                else if (
                    existingOccurrence.AccessibilityRemediationItemId !=
                        remediationItem.Id)
                {
                    throw new InvalidOperationException(
                        "A matched finding is already linked to another " +
                        "remediation item.");
                }
            }

            AccessibilityRemediationRetest retest =
                new()
                {
                    AccessibilityRemediationItemId =
                        remediationItem.Id,

                    AuthenticatedAuditRunId =
                        retestAuditRunId,

                    AuthenticatedAuditStepId =
                        comparisonItem.RetestStepId,

                    MatchedAuthenticatedAuditFindingId =
                        comparisonItem
                            .MatchedAuthenticatedAuditFindingId,

                    Result =
                        comparisonItem.Result,

                    RetestType =
                        "FullWorkflow",

                    MatchMethod =
                        comparisonItem.MatchMethod,

                    MatchConfidence =
                        comparisonItem.MatchConfidence,

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
                remediationItem.Status;

            string eventType;

            /*
             * A tracked issue that is detected again cannot remain Fixed
             * or Verified.
             */
            if (comparisonItem.Result ==
                    AccessibilityRemediationRetestResult.Detected &&
                (remediationItem.Status ==
                    AccessibilityRemediationStatus.Fixed ||
                 remediationItem.Status ==
                    AccessibilityRemediationStatus.Verified))
            {
                remediationItem.Status =
                    AccessibilityRemediationStatus.InProgress;

                eventType =
                    "Reopened";

                reopenedCount++;
            }
            else
            {
                eventType =
                    comparisonItem.Result switch
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

            remediationItem.UpdatedAt =
                now;

            AccessibilityRemediationMatchResult match =
                new()
                {
                    Result =
                        comparisonItem.Result,

                    MatchedAuthenticatedAuditFindingId =
                        comparisonItem
                            .MatchedAuthenticatedAuditFindingId,

                    MatchMethod =
                        comparisonItem.MatchMethod,

                    MatchConfidence =
                        comparisonItem.MatchConfidence,

                    Message =
                        comparisonItem.Message
                };

            string historyNotes =
                $"Full workflow retest using authenticated audit " +
                $"run #{retestAuditRunId}. " +
                BuildHistoryNotes(
                    match,
                    cleanedNotes);

            if (historyNotes.Length > 4000)
            {
                historyNotes =
                    historyNotes[..4000];
            }

            remediationItem.History.Add(
                new AccessibilityRemediationHistory
                {
                    EventType =
                        eventType,

                    PreviousStatus =
                        previousStatus,

                    NewStatus =
                        remediationItem.Status,

                    PreviousAssignee =
                        remediationItem.AssignedTo,

                    NewAssignee =
                        remediationItem.AssignedTo,

                    Notes =
                        historyNotes,

                    ChangedAt =
                        now,

                    ChangedBy =
                        cleanedRetestedBy
                });
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return new AccessibilityWorkflowRetestApplyResult
        {
            OriginalAuditRunId =
                originalAuditRunId,

            RetestAuditRunId =
                retestAuditRunId,

            TotalTracked =
                comparison.TotalTracked,

            StillDetected =
                comparison.StillDetected,

            NotDetected =
                comparison.NotDetected,

            Inconclusive =
                comparison.Inconclusive,

            Failed =
                comparison.Failed,

            Reopened =
                reopenedCount
        };
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

public sealed class AccessibilityWorkflowRetestApplyResult
{
    public int OriginalAuditRunId { get; init; }

    public int RetestAuditRunId { get; init; }

    public int TotalTracked { get; init; }

    public int StillDetected { get; init; }

    public int NotDetected { get; init; }

    public int Inconclusive { get; init; }

    public int Failed { get; init; }

    public int Reopened { get; init; }
}