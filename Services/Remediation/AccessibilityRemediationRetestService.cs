using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Services.Remediation;

public sealed class AccessibilityRemediationRetestService
{
    private const string CurrentStateRetestType =
        "CurrentState";

    private const string FullWorkflowRetestType =
        "FullWorkflow";

    private readonly ApplicationDbContext _dbContext;

    private readonly AccessibilityRemediationMatcher
        _matcher;

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
        _workflowComparisonService =
            workflowComparisonService;
    }

    /// <summary>
    /// Records a retest against one newly scanned authenticated
    /// page/workflow state.
    /// </summary>
    public async Task<AccessibilityRemediationRetest>
        RecordRetestAsync(
            int remediationItemId,
            int authenticatedAuditStepId,
            string? notes = null,
            string? retestedBy = null,
            CancellationToken cancellationToken = default)
    {
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

        int? authenticatedAuditRunId =
            await _dbContext.AuthenticatedAuditSteps
                .AsNoTracking()
                .Where(step =>
                    step.Id ==
                    authenticatedAuditStepId)
                .Select(step =>
                    (int?)step.AuthenticatedAuditRunId)
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (!authenticatedAuditRunId.HasValue)
        {
            throw new InvalidOperationException(
                "The authenticated audit step used for the " +
                "retest could not be found.");
        }

        /*
         * Current-state retesting historically uses the earliest
         * occurrence attached to the durable remediation item.
         *
         * Save that exact source finding AND pass the same id into
         * the matcher so matching and historical evidence cannot
         * accidentally disagree.
         */
        int? originalAuthenticatedAuditFindingId =
            await _dbContext
                .AccessibilityRemediationFindingOccurrences
                .AsNoTracking()
                .Where(occurrence =>
                    occurrence
                        .AccessibilityRemediationItemId ==
                    remediationItemId)
                .OrderBy(occurrence =>
                    occurrence.LinkedAt)
                .ThenBy(occurrence =>
                    occurrence.Id)
                .Select(occurrence =>
                    (int?)occurrence
                        .AuthenticatedAuditFindingId)
                .FirstOrDefaultAsync(
                    cancellationToken);

        AccessibilityRemediationMatchResult match =
            await _matcher.MatchAsync(
                remediationItemId,
                authenticatedAuditStepId,
                cancellationToken,
                originalAuthenticatedAuditFindingId:
                    originalAuthenticatedAuditFindingId);

        string? cleanedNotes =
            CleanOptionalText(
                notes,
                4000);

        string? cleanedRetestedBy =
            CleanOptionalText(
                retestedBy,
                200);

        DateTime now =
            DateTime.UtcNow;

        await using var transaction =
            await _dbContext.Database
                .BeginTransactionAsync(
                    cancellationToken);

        if (match.Result ==
                AccessibilityRemediationRetestResult.Detected &&
            match.MatchedAuthenticatedAuditFindingId.HasValue)
        {
            await LinkMatchedFindingAsync(
                remediationItemId,
                match.MatchedAuthenticatedAuditFindingId.Value,
                match.MatchMethod,
                match.MatchConfidence,
                cleanedRetestedBy,
                now,
                cancellationToken);
        }

        AccessibilityRemediationRetest retest =
            new()
            {
                AccessibilityRemediationItemId =
                    remediationItemId,

                AuthenticatedAuditRunId =
                    authenticatedAuditRunId.Value,

                AuthenticatedAuditStepId =
                    authenticatedAuditStepId,

                OriginalAuthenticatedAuditFindingId =
                    originalAuthenticatedAuditFindingId,

                MatchedAuthenticatedAuditFindingId =
                    match.MatchedAuthenticatedAuditFindingId,

                Result =
                    match.Result,

                RetestType =
                    CurrentStateRetestType,

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

        _dbContext.AccessibilityRemediationRetests
            .Add(retest);

        AccessibilityRemediationStatus previousStatus =
            item.Status;

        string eventType =
            ApplyRetestLifecycle(
                item,
                match.Result,
                out _);

        item.UpdatedAt =
            now;

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

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw new InvalidOperationException(
                "The remediation item changed while the retest " +
                "was being saved. Reload the item and try again.");
        }

        return retest;
    }

    /// <summary>
    /// Compares a completed authenticated workflow audit with an
    /// earlier audit and saves the comparison as formal retest evidence.
    /// </summary>
    public async Task<AccessibilityWorkflowRetestApplyResult>
        ApplyWorkflowRetestAsync(
            int originalAuditRunId,
            int retestAuditRunId,
            string? notes = null,
            string? retestedBy = null,
            CancellationToken cancellationToken = default)
    {
        AccessibilityRemediationWorkflowComparisonResult
            comparison =
                await _workflowComparisonService.CompareAsync(
                    originalAuditRunId,
                    retestAuditRunId,
                    cancellationToken);

        if (comparison.TotalTracked == 0)
        {
            throw new InvalidOperationException(
                "The original audit does not have any tracked " +
                "remediation items.");
        }

        string? cleanedNotes =
            CleanOptionalText(
                notes,
                4000);

        string? cleanedRetestedBy =
            CleanOptionalText(
                retestedBy,
                200);

        List<int> remediationItemIds =
            comparison.Items
                .Select(item =>
                    item.RemediationItemId)
                .Distinct()
                .ToList();

        if (remediationItemIds.Count == 0)
        {
            throw new InvalidOperationException(
                "The workflow comparison did not contain any " +
                "remediation items.");
        }

        await using var transaction =
            await _dbContext.Database
                .BeginTransactionAsync(
                    cancellationToken);

        /*
         * The same authenticated audit run must not be formally
         * applied repeatedly to the same remediation work.
         */
        bool alreadyApplied =
            await _dbContext.AccessibilityRemediationRetests
                .AsNoTracking()
                .AnyAsync(
                    retest =>
                        retest.RetestType ==
                            FullWorkflowRetestType &&
                        retest.AuthenticatedAuditRunId ==
                            retestAuditRunId &&
                        remediationItemIds.Contains(
                            retest
                                .AccessibilityRemediationItemId),
                    cancellationToken);

        if (alreadyApplied)
        {
            throw new InvalidOperationException(
                "This authenticated audit run has already been used " +
                "to formally retest one or more of these remediation items.");
        }

        Dictionary<int, AccessibilityRemediationItem>
            remediationItems =
                await _dbContext
                    .AccessibilityRemediationItems
                    .Where(item =>
                        remediationItemIds.Contains(
                            item.Id))
                    .ToDictionaryAsync(
                        item =>
                            item.Id,
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

        foreach (
            AccessibilityRemediationWorkflowComparisonItem
                comparisonItem
            in comparison.Items)
        {
            if (!remediationItems.TryGetValue(
                comparisonItem.RemediationItemId,
                out AccessibilityRemediationItem?
                    remediationItem))
            {
                throw new InvalidOperationException(
                    "A remediation item from the workflow comparison " +
                    "could not be loaded.");
            }

            if (comparisonItem.Result ==
                    AccessibilityRemediationRetestResult.Detected &&
                comparisonItem
                    .MatchedAuthenticatedAuditFindingId
                    .HasValue)
            {
                await LinkMatchedFindingAsync(
                    remediationItem.Id,
                    comparisonItem
                        .MatchedAuthenticatedAuditFindingId
                        .Value,
                    comparisonItem.MatchMethod,
                    comparisonItem.MatchConfidence,
                    cleanedRetestedBy,
                    now,
                    cancellationToken);
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

                    OriginalAuthenticatedAuditFindingId =
                        comparisonItem
                            .OriginalAuthenticatedAuditFindingId,

                    MatchedAuthenticatedAuditFindingId =
                        comparisonItem
                            .MatchedAuthenticatedAuditFindingId,

                    Result =
                        comparisonItem.Result,

                    RetestType =
                        FullWorkflowRetestType,

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

            _dbContext.AccessibilityRemediationRetests
                .Add(retest);

            AccessibilityRemediationStatus previousStatus =
                remediationItem.Status;

            string eventType =
                ApplyRetestLifecycle(
                    remediationItem,
                    comparisonItem.Result,
                    out bool reopened);

            if (reopened)
            {
                reopenedCount++;
            }

            remediationItem.UpdatedAt =
                now;

            AccessibilityRemediationMatchResult
                historyMatch =
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
                BuildFullWorkflowHistoryNotes(
                    retestAuditRunId,
                    historyMatch,
                    cleanedNotes);

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

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw new InvalidOperationException(
                "One or more remediation items changed while the " +
                "formal workflow retest was being saved. Reload the " +
                "workflow comparison and try again.");
        }

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

    /// <summary>
    /// Explicitly verifies several Fixed – Awaiting Verification
    /// remediation items using one saved formal workflow retest.
    /// </summary>
    public async Task<AccessibilityWorkflowVerificationResult>
        VerifyWorkflowItemsAsync(
            int retestAuditRunId,
            IEnumerable<int> remediationItemIds,
            string? notes = null,
            string? verifiedBy = null,
            CancellationToken cancellationToken = default)
    {
        if (remediationItemIds is null)
        {
            throw new InvalidOperationException(
                "Select at least one remediation item to verify.");
        }

        List<int> selectedItemIds =
            remediationItemIds
                .Where(id =>
                    id > 0)
                .Distinct()
                .ToList();

        if (selectedItemIds.Count == 0)
        {
            throw new InvalidOperationException(
                "Select at least one remediation item to verify.");
        }

        string? cleanedNotes =
            CleanOptionalText(
                notes,
                4000);

        string? cleanedVerifiedBy =
            CleanOptionalText(
                verifiedBy,
                200);

        List<AccessibilityRemediationItem> items =
            await _dbContext.AccessibilityRemediationItems
                .Where(item =>
                    selectedItemIds.Contains(
                        item.Id))
                .Include(item =>
                    item.Retests)
                .ToListAsync(
                    cancellationToken);

        if (items.Count !=
            selectedItemIds.Count)
        {
            throw new InvalidOperationException(
                "One or more selected remediation items could not be found.");
        }

        Dictionary<int, AccessibilityRemediationRetest>
            workflowRetestsByItem =
                new();

        foreach (AccessibilityRemediationItem item
            in items)
        {
            if (item.Status !=
                AccessibilityRemediationStatus.Fixed)
            {
                throw new InvalidOperationException(
                    $"Remediation item #{item.Id} is not awaiting verification.");
            }

            AccessibilityRemediationRetest?
                workflowRetest =
                    item.Retests
                        .Where(retest =>
                            retest.RetestType ==
                                FullWorkflowRetestType &&
                            retest.AuthenticatedAuditRunId ==
                                retestAuditRunId)
                        .OrderByDescending(retest =>
                            retest.RetestedAt)
                        .ThenByDescending(retest =>
                            retest.Id)
                        .FirstOrDefault();

            if (workflowRetest is null)
            {
                throw new InvalidOperationException(
                    $"Remediation item #{item.Id} does not have a saved " +
                    "full-workflow retest for this audit run.");
            }

            if (workflowRetest.Result !=
                AccessibilityRemediationRetestResult.NotDetected)
            {
                throw new InvalidOperationException(
                    $"Remediation item #{item.Id} cannot be verified " +
                    "because the workflow retest did not return Not Detected.");
            }

            AccessibilityRemediationRetest?
                latestRetest =
                    item.Retests
                        .OrderByDescending(retest =>
                            retest.RetestedAt)
                        .ThenByDescending(retest =>
                            retest.Id)
                        .FirstOrDefault();

            if (latestRetest is null ||
                latestRetest.Id !=
                    workflowRetest.Id)
            {
                throw new InvalidOperationException(
                    $"Remediation item #{item.Id} has newer retest evidence. " +
                    "Review the remediation item before verifying it.");
            }

            workflowRetestsByItem[item.Id] =
                workflowRetest;
        }

        DateTime now =
            DateTime.UtcNow;

        await using var transaction =
            await _dbContext.Database
                .BeginTransactionAsync(
                    cancellationToken);

        foreach (AccessibilityRemediationItem item
            in items)
        {
            AccessibilityRemediationRetest workflowRetest =
                workflowRetestsByItem[item.Id];

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
                        BuildVerificationHistoryNotes(
                            workflowRetest,
                            cleanedNotes),

                    ChangedAt =
                        now,

                    ChangedBy =
                        cleanedVerifiedBy
                });
        }

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw new InvalidOperationException(
                "One or more selected remediation items changed while " +
                "verification was being saved. Reload the workflow " +
                "retest details and try again.");
        }

        return new AccessibilityWorkflowVerificationResult
        {
            RetestAuditRunId =
                retestAuditRunId,

            Selected =
                selectedItemIds.Count,

            Verified =
                items.Count
        };
    }

    /// <summary>
    /// Explicitly verifies one Fixed – Awaiting Verification
    /// remediation item from its latest successful retest evidence.
    /// </summary>
    public async Task VerifyAsync(
        int remediationItemId,
        string? notes = null,
        string? verifiedBy = null,
        CancellationToken cancellationToken = default)
    {
        AccessibilityRemediationItem? item =
            await _dbContext.AccessibilityRemediationItems
                .Include(item =>
                    item.Retests)
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

        if (item.Status !=
            AccessibilityRemediationStatus.Fixed)
        {
            throw new InvalidOperationException(
                "Only an item marked Fixed – Awaiting Verification " +
                "can be verified.");
        }

        AccessibilityRemediationRetest?
            latestRetest =
                item.Retests
                    .OrderByDescending(retest =>
                        retest.RetestedAt)
                    .ThenByDescending(retest =>
                        retest.Id)
                    .FirstOrDefault();

        if (latestRetest is null)
        {
            throw new InvalidOperationException(
                "This remediation item must be successfully retested " +
                "before it can be verified.");
        }

        if (latestRetest.Result !=
            AccessibilityRemediationRetestResult.NotDetected)
        {
            throw new InvalidOperationException(
                "The latest retest did not confirm that the issue " +
                "is no longer detected.");
        }

        string? cleanedNotes =
            CleanOptionalText(
                notes,
                4000);

        string? cleanedVerifiedBy =
            CleanOptionalText(
                verifiedBy,
                200);

        DateTime now =
            DateTime.UtcNow;

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
                    BuildVerificationHistoryNotes(
                        latestRetest,
                        cleanedNotes),

                ChangedAt =
                    now,

                ChangedBy =
                    cleanedVerifiedBy
            });

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(
                "The remediation item changed while verification was " +
                "being saved. Reload the item and try again.");
        }
    }

    /// <summary>
    /// Links a newly detected immutable authenticated finding to the
    /// existing durable remediation item.
    ///
    /// One immutable finding may belong to only one remediation item.
    /// </summary>
    private async Task LinkMatchedFindingAsync(
        int remediationItemId,
        int matchedFindingId,
        string? matchMethod,
        decimal? matchConfidence,
        string? linkedBy,
        DateTime linkedAt,
        CancellationToken cancellationToken)
    {
        AccessibilityRemediationFindingOccurrence?
            existingOccurrence =
                await _dbContext
                    .AccessibilityRemediationFindingOccurrences
                    .FirstOrDefaultAsync(
                        occurrence =>
                            occurrence
                                .AuthenticatedAuditFindingId ==
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
                            CleanOptionalText(
                                matchMethod,
                                100),

                        MatchConfidence =
                            matchConfidence,

                        LinkedAt =
                            linkedAt,

                        LinkedBy =
                            CleanOptionalText(
                                linkedBy,
                                200)
                    });

            return;
        }

        if (existingOccurrence
                .AccessibilityRemediationItemId !=
            remediationItemId)
        {
            throw new InvalidOperationException(
                "The matched accessibility finding is already linked " +
                "to another remediation item.");
        }
    }

    /// <summary>
    /// Applies the remediation-status side effects of one retest.
    ///
    /// A positive re-detection reopens Fixed or Verified work.
    /// NotDetected does not automatically Verify anything.
    /// Verification remains an explicit separate action.
    /// </summary>
    private static string ApplyRetestLifecycle(
        AccessibilityRemediationItem item,
        AccessibilityRemediationRetestResult result,
        out bool reopened)
    {
        reopened =
            false;

        if (result ==
                AccessibilityRemediationRetestResult.Detected &&
            (
                item.Status ==
                    AccessibilityRemediationStatus.Fixed ||
                item.Status ==
                    AccessibilityRemediationStatus.Verified
            ))
        {
            item.Status =
                AccessibilityRemediationStatus.InProgress;

            reopened =
                true;

            return "Reopened";
        }

        return result switch
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

    private static string BuildHistoryNotes(
        AccessibilityRemediationMatchResult match,
        string? userNotes)
    {
        List<string> parts =
            new()
            {
                $"Retest result: {match.Result}.",

                $"Match method: " +
                $"{(string.IsNullOrWhiteSpace(match.MatchMethod)
                    ? "Unknown"
                    : match.MatchMethod)}.",

                $"Match confidence: {match.MatchConfidence:P0}."
            };

        if (!string.IsNullOrWhiteSpace(
            match.Message))
        {
            parts.Add(
                match.Message.Trim());
        }

        if (!string.IsNullOrWhiteSpace(
            userNotes))
        {
            parts.Add(
                $"Notes: {userNotes.Trim()}");
        }

        return CombineHistoryParts(
            parts);
    }

    private static string BuildFullWorkflowHistoryNotes(
        int retestAuditRunId,
        AccessibilityRemediationMatchResult match,
        string? userNotes)
    {
        string detail =
            BuildHistoryNotes(
                match,
                userNotes);

        return LimitLength(
            $"Full workflow retest using authenticated audit " +
            $"run #{retestAuditRunId}. {detail}",
            4000);
    }

    private static string BuildVerificationHistoryNotes(
        AccessibilityRemediationRetest retest,
        string? userNotes)
    {
        List<string> parts =
            new();

        if (string.Equals(
            retest.RetestType,
            FullWorkflowRetestType,
            StringComparison.Ordinal))
        {
            if (retest.AuthenticatedAuditRunId.HasValue)
            {
                parts.Add(
                    "Verified after full workflow retest " +
                    $"audit run #{retest.AuthenticatedAuditRunId.Value} " +
                    "no longer detected the tracked issue.");
            }
            else
            {
                parts.Add(
                    "Verified after the latest full workflow retest " +
                    "no longer detected the tracked issue.");
            }
        }
        else
        {
            parts.Add(
                "Verified after the latest current-state retest " +
                "no longer detected the tracked accessibility issue.");
        }

        parts.Add(
            $"Retest #{retest.Id}.");

        if (!string.IsNullOrWhiteSpace(
            retest.MatchMethod))
        {
            parts.Add(
                $"Match method: {retest.MatchMethod}.");
        }

        if (retest.MatchConfidence.HasValue)
        {
            parts.Add(
                $"Match confidence: " +
                $"{retest.MatchConfidence.Value:P0}.");
        }

        if (!string.IsNullOrWhiteSpace(
            userNotes))
        {
            parts.Add(
                $"Notes: {userNotes.Trim()}");
        }

        return CombineHistoryParts(
            parts);
    }

    private static string CombineHistoryParts(
        IEnumerable<string> parts)
    {
        string combined =
            string.Join(
                " ",
                parts.Where(part =>
                    !string.IsNullOrWhiteSpace(
                        part)));

        return LimitLength(
            combined,
            4000);
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

        return LimitLength(
            value.Trim(),
            maximumLength);
    }

    private static string LimitLength(
        string value,
        int maximumLength)
    {
        if (value.Length <=
            maximumLength)
        {
            return value;
        }

        return value[..maximumLength];
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

public sealed class AccessibilityWorkflowVerificationResult
{
    public int RetestAuditRunId { get; init; }

    public int Selected { get; init; }

    public int Verified { get; init; }
}
