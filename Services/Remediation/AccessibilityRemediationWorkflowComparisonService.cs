using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Services.Remediation;

public sealed class AccessibilityRemediationWorkflowComparisonService
{
    private readonly ApplicationDbContext _dbContext;

    private readonly AccessibilityRemediationMatcher
        _matcher;


    public AccessibilityRemediationWorkflowComparisonService(
        ApplicationDbContext dbContext,
        AccessibilityRemediationMatcher matcher)
    {
        _dbContext =
            dbContext;

        _matcher =
            matcher;
    }


    public async Task<
        AccessibilityRemediationWorkflowComparisonResult>
        CompareAsync(
            int originalAuditRunId,
            int retestAuditRunId,
            CancellationToken cancellationToken = default)
    {
        if (originalAuditRunId ==
            retestAuditRunId)
        {
            throw new InvalidOperationException(
                "The original audit and retest audit must be " +
                "different runs.");
        }


        List<AuthenticatedAuditRun> runs =
            await _dbContext.AuthenticatedAuditRuns
                .AsNoTracking()
                .Where(run =>
                    run.Id ==
                        originalAuditRunId ||
                    run.Id ==
                        retestAuditRunId)
                .ToListAsync(
                    cancellationToken);


        AuthenticatedAuditRun? originalRun =
            runs.FirstOrDefault(run =>
                run.Id ==
                    originalAuditRunId);


        if (originalRun is null)
        {
            throw new InvalidOperationException(
                "The original authenticated audit run could not " +
                "be found.");
        }


        AuthenticatedAuditRun? retestRun =
            runs.FirstOrDefault(run =>
                run.Id ==
                    retestAuditRunId);


        if (retestRun is null)
        {
            throw new InvalidOperationException(
                "The authenticated workflow retest run could not " +
                "be found.");
        }


        if (!string.Equals(
            originalRun.ApplicationName.Trim(),
            retestRun.ApplicationName.Trim(),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The original audit and retest audit belong to " +
                "different applications.");
        }


        if (!string.Equals(
            retestRun.Status,
            "Completed",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected workflow retest must be a completed " +
                "authenticated audit.");
        }


        if (retestRun.StartedAt <=
            originalRun.StartedAt)
        {
            throw new InvalidOperationException(
                "The workflow retest must be newer than the " +
                "original authenticated audit.");
        }


        /*
         * State counts are informational only.
         *
         * A later workflow may legitimately contain more or fewer
         * rendered states than the original audit.
         *
         * State identity is resolved by AccessibilityRemediationMatcher
         * using page evidence rather than requiring matching step
         * numbers or matching workflow lengths.
         */
        int originalStateCount =
            await _dbContext.AuthenticatedAuditSteps
                .AsNoTracking()
                .CountAsync(
                    step =>
                        step.AuthenticatedAuditRunId ==
                            originalAuditRunId,
                    cancellationToken);


        int retestStateCount =
            await _dbContext.AuthenticatedAuditSteps
                .AsNoTracking()
                .CountAsync(
                    step =>
                        step.AuthenticatedAuditRunId ==
                            retestAuditRunId,
                    cancellationToken);


        /*
         * Load EVERY saved accessibility finding from the original
         * audit.
         *
         * This is the key difference from the old implementation.
         *
         * The old comparison began with AccessibilityRemediationItems,
         * which meant an audit containing accessibility findings but no
         * manually tracked remediation items appeared to contain
         * "No Tracked Findings."
         *
         * Audit comparison should not require remediation tracking.
         */
        List<AuthenticatedAuditFinding> originalFindings =
            await _dbContext.AuthenticatedAuditFindings
                .AsNoTracking()
                .Where(finding =>
                    finding
                        .AuthenticatedAuditStep
                        .AuthenticatedAuditRunId ==
                    originalAuditRunId)
                .Include(finding =>
                    finding.Nodes)
                .Include(finding =>
                    finding.AuthenticatedAuditStep)
                    .ThenInclude(step =>
                        step.AuthenticatedAuditRun)
                .ToListAsync(
                    cancellationToken);


        originalFindings =
            originalFindings
                .OrderBy(finding =>
                    finding
                        .AuthenticatedAuditStep
                        .StepNumber)
                .ThenBy(finding =>
                    finding.PriorityRank)
                .ThenBy(finding =>
                    finding.RuleId)
                .ThenBy(finding =>
                    finding.Id)
                .ToList();


        List<int> originalFindingIds =
            originalFindings
                .Select(finding =>
                    finding.Id)
                .ToList();


        /*
         * Load remediation relationships separately.
         *
         * These relationships tell us whether an original finding is
         * already being tracked, but they no longer determine whether
         * that finding is allowed to participate in an audit
         * comparison.
         */
        List<AccessibilityRemediationFindingOccurrence>
            originalTrackingOccurrences =
                originalFindingIds.Count == 0
                    ? new List<
                        AccessibilityRemediationFindingOccurrence>()
                    : await _dbContext
                        .AccessibilityRemediationFindingOccurrences
                        .AsNoTracking()
                        .Where(occurrence =>
                            originalFindingIds.Contains(
                                occurrence
                                    .AuthenticatedAuditFindingId))
                        .Include(occurrence =>
                            occurrence.RemediationItem)
                        .OrderBy(occurrence =>
                            occurrence.LinkedAt)
                        .ThenBy(occurrence =>
                            occurrence.Id)
                        .ToListAsync(
                            cancellationToken);


        /*
         * A finding should normally belong to one durable remediation
         * item.
         *
         * If development data contains more than one relationship,
         * use the earliest saved occurrence as the display relationship.
         */
        Dictionary<int,
            AccessibilityRemediationFindingOccurrence>
            trackingOccurrenceByFindingId =
                originalTrackingOccurrences
                    .GroupBy(occurrence =>
                        occurrence
                            .AuthenticatedAuditFindingId)
                    .ToDictionary(
                        group =>
                            group.Key,
                        group =>
                            group
                                .OrderBy(occurrence =>
                                    occurrence.LinkedAt)
                                .ThenBy(occurrence =>
                                    occurrence.Id)
                                .First());


        /*
         * Build the full audit-to-audit preview.
         *
         * This contains tracked AND untracked original findings.
         */
        List<AccessibilityAuditWorkflowComparisonItem>
            auditComparisonItems =
                new();


        foreach (AuthenticatedAuditFinding originalFinding
            in originalFindings)
        {
            AuthenticatedAuditStep originalStep =
                originalFinding
                    .AuthenticatedAuditStep;


            trackingOccurrenceByFindingId.TryGetValue(
                originalFinding.Id,
                out AccessibilityRemediationFindingOccurrence?
                    trackingOccurrence);


            AccessibilityRemediationStateMatch? stateMatch =
                await _matcher
                    .FindBestMatchingStepForFindingAsync(
                        originalFinding.Id,
                        retestAuditRunId,
                        cancellationToken);


            if (stateMatch is null)
            {
                auditComparisonItems.Add(
                    new AccessibilityAuditWorkflowComparisonItem
                    {
                        OriginalAuthenticatedAuditFindingId =
                            originalFinding.Id,

                        RemediationItemId =
                            trackingOccurrence?
                                .AccessibilityRemediationItemId,

                        CurrentStatus =
                            trackingOccurrence?
                                .RemediationItem
                                .Status,

                        RuleId =
                            originalFinding.RuleId,

                        FindingType =
                            originalFinding.FindingType,

                        Impact =
                            originalFinding.Impact,

                        OriginalStepNumber =
                            originalStep.StepNumber,

                        OriginalStepName =
                            originalStep.StepName,

                        OriginalUrl =
                            originalStep.Url,

                        Result =
                            AccessibilityRemediationRetestResult
                                .Inconclusive,

                        MatchMethod =
                            "WorkflowStateNotFound",

                        MatchConfidence =
                            0m,

                        Message =
                            "A sufficiently similar page or workflow " +
                            "state could not be found in the later " +
                            "audit."
                    });

                continue;
            }


            AccessibilityRemediationMatchResult match =
                await _matcher.MatchFindingAsync(
                    originalFinding.Id,
                    stateMatch.AuthenticatedAuditStepId,
                    cancellationToken);


            auditComparisonItems.Add(
                new AccessibilityAuditWorkflowComparisonItem
                {
                    OriginalAuthenticatedAuditFindingId =
                        originalFinding.Id,

                    RemediationItemId =
                        trackingOccurrence?
                            .AccessibilityRemediationItemId,

                    CurrentStatus =
                        trackingOccurrence?
                            .RemediationItem
                            .Status,

                    RuleId =
                        originalFinding.RuleId,

                    FindingType =
                        originalFinding.FindingType,

                    Impact =
                        originalFinding.Impact,

                    OriginalStepNumber =
                        originalStep.StepNumber,

                    OriginalStepName =
                        originalStep.StepName,

                    OriginalUrl =
                        originalStep.Url,

                    RetestStepId =
                        stateMatch
                            .AuthenticatedAuditStepId,

                    RetestStepNumber =
                        stateMatch.StepNumber,

                    RetestStepName =
                        stateMatch.StepName,

                    RetestUrl =
                        stateMatch.Url,

                    StateConfidence =
                        stateMatch.StateConfidence,

                    Result =
                        match.Result,

                    MatchedAuthenticatedAuditFindingId =
                        match
                            .MatchedAuthenticatedAuditFindingId,

                    MatchMethod =
                        match.MatchMethod,

                    MatchConfidence =
                        match.MatchConfidence,

                    Message =
                        match.Message
                });
        }


        /*
         * Formal remediation retesting must still operate only on
         * durable remediation items.
         *
         * A remediation item can accumulate later occurrences over
         * time. For a selected original audit we preserve the original
         * behavior of choosing one source finding per remediation item.
         */
        List<AccessibilityRemediationFindingOccurrence>
            formalSourceOccurrences =
                originalTrackingOccurrences
                    .GroupBy(occurrence =>
                        occurrence
                            .AccessibilityRemediationItemId)
                    .Select(group =>
                        group
                            .OrderBy(occurrence =>
                                occurrence.LinkedAt)
                            .ThenBy(occurrence =>
                                occurrence.Id)
                            .First())
                    .OrderBy(occurrence =>
                        occurrence
                            .AccessibilityRemediationItemId)
                    .ToList();


        Dictionary<int,
            AccessibilityAuditWorkflowComparisonItem>
            auditItemByOriginalFindingId =
                auditComparisonItems
                    .GroupBy(item =>
                        item
                            .OriginalAuthenticatedAuditFindingId)
                    .ToDictionary(
                        group =>
                            group.Key,
                        group =>
                            group.First());


        List<AccessibilityRemediationWorkflowComparisonItem>
            trackedComparisonItems =
                new();


        foreach (
            AccessibilityRemediationFindingOccurrence
                sourceOccurrence
            in formalSourceOccurrences)
        {
            if (!auditItemByOriginalFindingId.TryGetValue(
                sourceOccurrence.AuthenticatedAuditFindingId,
                out AccessibilityAuditWorkflowComparisonItem?
                    auditItem))
            {
                continue;
            }


            trackedComparisonItems.Add(
                new AccessibilityRemediationWorkflowComparisonItem
                {
                    RemediationItemId =
                        sourceOccurrence
                            .AccessibilityRemediationItemId,

                    OriginalAuthenticatedAuditFindingId =
                        auditItem
                            .OriginalAuthenticatedAuditFindingId,

                    CurrentStatus =
                        sourceOccurrence
                            .RemediationItem
                            .Status,

                    RuleId =
                        auditItem.RuleId,

                    FindingType =
                        auditItem.FindingType,

                    Impact =
                        auditItem.Impact,

                    OriginalStepNumber =
                        auditItem.OriginalStepNumber,

                    OriginalStepName =
                        auditItem.OriginalStepName,

                    OriginalUrl =
                        auditItem.OriginalUrl,

                    RetestStepId =
                        auditItem.RetestStepId,

                    RetestStepNumber =
                        auditItem.RetestStepNumber,

                    RetestStepName =
                        auditItem.RetestStepName,

                    RetestUrl =
                        auditItem.RetestUrl,

                    StateConfidence =
                        auditItem.StateConfidence,

                    Result =
                        auditItem.Result,

                    MatchedAuthenticatedAuditFindingId =
                        auditItem
                            .MatchedAuthenticatedAuditFindingId,

                    MatchMethod =
                        auditItem.MatchMethod,

                    MatchConfidence =
                        auditItem.MatchConfidence,

                    Message =
                        auditItem.Message
                });
        }


        return new
            AccessibilityRemediationWorkflowComparisonResult
        {
            OriginalAuditRunId =
                originalAuditRunId,

            RetestAuditRunId =
                retestAuditRunId,

            ApplicationName =
                originalRun.ApplicationName,

            OriginalAuditStartedAt =
                originalRun.StartedAt,

            RetestAuditStartedAt =
                retestRun.StartedAt,

            RetestAuditStatus =
                retestRun.Status,

            OriginalStateCount =
                originalStateCount,

            RetestStateCount =
                retestStateCount,

            /*
             * Full preview used by the Workflow Retests screen.
             */
            AuditItems =
                auditComparisonItems
                    .OrderBy(item =>
                        GetResultOrder(
                            item.Result))
                    .ThenBy(item =>
                        item.OriginalStepNumber)
                    .ThenBy(item =>
                        item.RuleId)
                    .ThenBy(item =>
                        item
                            .OriginalAuthenticatedAuditFindingId)
                    .ToList(),

            /*
             * Tracked subset used by formal remediation retest writes.
             */
            Items =
                trackedComparisonItems
                    .OrderBy(item =>
                        GetResultOrder(
                            item.Result))
                    .ThenBy(item =>
                        item.OriginalStepNumber)
                    .ThenBy(item =>
                        item.RuleId)
                    .ThenBy(item =>
                        item
                            .OriginalAuthenticatedAuditFindingId)
                    .ToList()
        };
    }


    private static int GetResultOrder(
        AccessibilityRemediationRetestResult result)
    {
        return result switch
        {
            AccessibilityRemediationRetestResult
                .Detected =>
                    0,

            AccessibilityRemediationRetestResult
                .Failed =>
                    1,

            AccessibilityRemediationRetestResult
                .Inconclusive =>
                    2,

            AccessibilityRemediationRetestResult
                .NotDetected =>
                    3,

            _ =>
                4
        };
    }
}


/*
 * Result returned by the comparison service.
 *
 * AuditItems:
 *     Every saved violation / needs-review finding from the selected
 *     original audit. Used for read-only audit comparison.
 *
 * Items:
 *     Only findings connected to durable remediation items. Used by
 *     the formal remediation retest workflow.
 */
public sealed class AccessibilityRemediationWorkflowComparisonResult
{
    public int OriginalAuditRunId
    {
        get;
        init;
    }


    public int RetestAuditRunId
    {
        get;
        init;
    }


    public string ApplicationName
    {
        get;
        init;
    } = string.Empty;


    public DateTime OriginalAuditStartedAt
    {
        get;
        init;
    }


    public DateTime RetestAuditStartedAt
    {
        get;
        init;
    }


    public string RetestAuditStatus
    {
        get;
        init;
    } = string.Empty;


    public int OriginalStateCount
    {
        get;
        init;
    }


    public int RetestStateCount
    {
        get;
        init;
    }


    public bool StateCountDiffers =>
        OriginalStateCount !=
        RetestStateCount;


    /*
     * Full read-only audit comparison.
     */
    public IReadOnlyList<
        AccessibilityAuditWorkflowComparisonItem>
        AuditItems
    {
        get;
        init;
    } =
        Array.Empty<
            AccessibilityAuditWorkflowComparisonItem>();


    /*
     * Formal remediation subset.
     *
     * Keep this property for the existing Phase 6 formal retest
     * workflow.
     */
    public IReadOnlyList<
        AccessibilityRemediationWorkflowComparisonItem>
        Items
    {
        get;
        init;
    } =
        Array.Empty<
            AccessibilityRemediationWorkflowComparisonItem>();


    /*
     * Existing formal-remediation metrics.
     */
    public int TotalTracked =>
        Items.Count;


    public int StillDetected =>
        Items.Count(item =>
            item.Result ==
                AccessibilityRemediationRetestResult
                    .Detected);


    public int NotDetected =>
        Items.Count(item =>
            item.Result ==
                AccessibilityRemediationRetestResult
                    .NotDetected);


    public int Inconclusive =>
        Items.Count(item =>
            item.Result ==
                AccessibilityRemediationRetestResult
                    .Inconclusive);


    public int Failed =>
        Items.Count(item =>
            item.Result ==
                AccessibilityRemediationRetestResult
                    .Failed);


    /*
     * Full audit-comparison metrics.
     */
    public int TotalCompared =>
        AuditItems.Count;


    public int AuditStillDetected =>
        AuditItems.Count(item =>
            item.Result ==
                AccessibilityRemediationRetestResult
                    .Detected);


    public int AuditNotDetected =>
        AuditItems.Count(item =>
            item.Result ==
                AccessibilityRemediationRetestResult
                    .NotDetected);


    public int AuditInconclusive =>
        AuditItems.Count(item =>
            item.Result ==
                AccessibilityRemediationRetestResult
                    .Inconclusive);


    public int AuditFailed =>
        AuditItems.Count(item =>
            item.Result ==
                AccessibilityRemediationRetestResult
                    .Failed);


    public int TrackedFindingCount =>
        AuditItems.Count(item =>
            item.IsTracked);


    public int UntrackedFindingCount =>
        AuditItems.Count(item =>
            !item.IsTracked);
}


/*
 * One finding in the complete read-only audit comparison.
 *
 * RemediationItemId and CurrentStatus are nullable because ordinary
 * audit comparison must also work for findings that have never been
 * added to the Remediation Tracker.
 */
public sealed class AccessibilityAuditWorkflowComparisonItem
{
    public int OriginalAuthenticatedAuditFindingId
    {
        get;
        init;
    }


    public int? RemediationItemId
    {
        get;
        init;
    }


    public bool IsTracked =>
        RemediationItemId.HasValue;


    public AccessibilityRemediationStatus? CurrentStatus
    {
        get;
        init;
    }


    public string RuleId
    {
        get;
        init;
    } = string.Empty;


    public string FindingType
    {
        get;
        init;
    } = string.Empty;


    public string? Impact
    {
        get;
        init;
    }


    public int OriginalStepNumber
    {
        get;
        init;
    }


    public string? OriginalStepName
    {
        get;
        init;
    }


    public string OriginalUrl
    {
        get;
        init;
    } = string.Empty;


    public int? RetestStepId
    {
        get;
        init;
    }


    public int? RetestStepNumber
    {
        get;
        init;
    }


    public string? RetestStepName
    {
        get;
        init;
    }


    public string? RetestUrl
    {
        get;
        init;
    }


    public decimal? StateConfidence
    {
        get;
        init;
    }


    public AccessibilityRemediationRetestResult Result
    {
        get;
        init;
    }


    public int? MatchedAuthenticatedAuditFindingId
    {
        get;
        init;
    }


    public string MatchMethod
    {
        get;
        init;
    } = string.Empty;


    public decimal MatchConfidence
    {
        get;
        init;
    }


    public string? Message
    {
        get;
        init;
    }
}


/*
 * Existing formal-remediation comparison item.
 *
 * This remains non-nullable because anything entering the formal
 * remediation retest workflow must already be a tracked remediation
 * item.
 */
public sealed class AccessibilityRemediationWorkflowComparisonItem
{
    public int RemediationItemId
    {
        get;
        init;
    }


    public int OriginalAuthenticatedAuditFindingId
    {
        get;
        init;
    }


    public AccessibilityRemediationStatus CurrentStatus
    {
        get;
        init;
    }


    public string RuleId
    {
        get;
        init;
    } = string.Empty;


    public string FindingType
    {
        get;
        init;
    } = string.Empty;


    public string? Impact
    {
        get;
        init;
    }


    public int OriginalStepNumber
    {
        get;
        init;
    }


    public string? OriginalStepName
    {
        get;
        init;
    }


    public string OriginalUrl
    {
        get;
        init;
    } = string.Empty;


    public int? RetestStepId
    {
        get;
        init;
    }


    public int? RetestStepNumber
    {
        get;
        init;
    }


    public string? RetestStepName
    {
        get;
        init;
    }


    public string? RetestUrl
    {
        get;
        init;
    }


    public decimal? StateConfidence
    {
        get;
        init;
    }


    public AccessibilityRemediationRetestResult Result
    {
        get;
        init;
    }


    public int? MatchedAuthenticatedAuditFindingId
    {
        get;
        init;
    }


    public string MatchMethod
    {
        get;
        init;
    } = string.Empty;


    public decimal MatchConfidence
    {
        get;
        init;
    }


    public string? Message
    {
        get;
        init;
    }
}
