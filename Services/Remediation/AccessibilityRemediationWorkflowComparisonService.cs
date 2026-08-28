using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Services.Remediation;

public sealed class AccessibilityRemediationWorkflowComparisonService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly AccessibilityRemediationMatcher _matcher;

    public AccessibilityRemediationWorkflowComparisonService(
        ApplicationDbContext dbContext,
        AccessibilityRemediationMatcher matcher)
    {
        _dbContext = dbContext;
        _matcher = matcher;
    }

    public async Task<AccessibilityRemediationWorkflowComparisonResult>
        CompareAsync(
            int originalAuditRunId,
            int retestAuditRunId,
            CancellationToken cancellationToken = default)
    {
        if (originalAuditRunId == retestAuditRunId)
        {
            throw new InvalidOperationException(
                "The original audit and retest audit must be different runs.");
        }

        List<AuthenticatedAuditRun> runs =
            await _dbContext.AuthenticatedAuditRuns
                .AsNoTracking()
                .Where(run =>
                    run.Id == originalAuditRunId ||
                    run.Id == retestAuditRunId)
                .ToListAsync(cancellationToken);

        AuthenticatedAuditRun? originalRun =
            runs.FirstOrDefault(run =>
                run.Id == originalAuditRunId);

        if (originalRun is null)
        {
            throw new InvalidOperationException(
                "The original authenticated audit run could not be found.");
        }

        AuthenticatedAuditRun? retestRun =
            runs.FirstOrDefault(run =>
                run.Id == retestAuditRunId);

        if (retestRun is null)
        {
            throw new InvalidOperationException(
                "The authenticated workflow retest run could not be found.");
        }

        if (!string.Equals(
            originalRun.ApplicationName.Trim(),
            retestRun.ApplicationName.Trim(),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The original audit and retest audit belong to different applications.");
        }

        if (!string.Equals(
            retestRun.Status,
            "Completed",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected workflow retest must be a completed authenticated audit.");
        }

        if (retestRun.StartedAt <= originalRun.StartedAt)
        {
            throw new InvalidOperationException(
                "The workflow retest must be newer than the original authenticated audit.");
        }

        List<AccessibilityRemediationItem> remediationItems =
            await _dbContext.AccessibilityRemediationItems
                .AsNoTracking()
                .Where(item =>
                    item.FindingOccurrences.Any(occurrence =>
                        occurrence
                            .AuthenticatedAuditFinding
                            .AuthenticatedAuditStep
                            .AuthenticatedAuditRunId ==
                        originalAuditRunId))
                .Include(item =>
                    item.FindingOccurrences)
                    .ThenInclude(occurrence =>
                        occurrence.AuthenticatedAuditFinding)
                        .ThenInclude(finding =>
                            finding.AuthenticatedAuditStep)
                            .ThenInclude(step =>
                                step.AuthenticatedAuditRun)
                .ToListAsync(cancellationToken);

        List<AccessibilityRemediationWorkflowComparisonItem>
            comparisonItems =
                new();

        foreach (AccessibilityRemediationItem remediationItem
            in remediationItems)
        {
            AccessibilityRemediationFindingOccurrence?
                originalOccurrence =
                    remediationItem.FindingOccurrences
                        .Where(occurrence =>
                            occurrence
                                .AuthenticatedAuditFinding
                                .AuthenticatedAuditStep
                                .AuthenticatedAuditRunId ==
                            originalAuditRunId)
                        .OrderBy(occurrence =>
                            occurrence.LinkedAt)
                        .ThenBy(occurrence =>
                            occurrence.Id)
                        .FirstOrDefault();

            if (originalOccurrence is null)
            {
                continue;
            }

            AuthenticatedAuditFinding originalFinding =
                originalOccurrence.AuthenticatedAuditFinding;

            AuthenticatedAuditStep originalStep =
                originalFinding.AuthenticatedAuditStep;

            AccessibilityRemediationStateMatch? stateMatch =
                await _matcher.FindBestMatchingStepAsync(
                    remediationItem.Id,
                    retestAuditRunId,
                    cancellationToken);

            if (stateMatch is null)
            {
                comparisonItems.Add(
                    new AccessibilityRemediationWorkflowComparisonItem
                    {
                        RemediationItemId =
                            remediationItem.Id,

                        CurrentStatus =
                            remediationItem.Status,

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
                            "A sufficiently similar page or workflow state " +
                            "could not be found in the retest audit."
                    });

                continue;
            }

            AccessibilityRemediationMatchResult match =
                await _matcher.MatchAsync(
                    remediationItem.Id,
                    stateMatch.AuthenticatedAuditStepId,
                    cancellationToken);

            comparisonItems.Add(
                new AccessibilityRemediationWorkflowComparisonItem
                {
                    RemediationItemId =
                        remediationItem.Id,

                    CurrentStatus =
                        remediationItem.Status,

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
                        stateMatch.AuthenticatedAuditStepId,

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
                        match.MatchedAuthenticatedAuditFindingId,

                    MatchMethod =
                        match.MatchMethod,

                    MatchConfidence =
                        match.MatchConfidence,

                    Message =
                        match.Message
                });
        }

        return new AccessibilityRemediationWorkflowComparisonResult
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

            Items =
                comparisonItems
                    .OrderBy(item =>
                        GetResultOrder(item.Result))
                    .ThenBy(item =>
                        item.OriginalStepNumber)
                    .ThenBy(item =>
                        item.RuleId)
                    .ToList()
        };
    }

    private static int GetResultOrder(
        AccessibilityRemediationRetestResult result)
    {
        return result switch
        {
            AccessibilityRemediationRetestResult.Detected =>
                0,

            AccessibilityRemediationRetestResult.Failed =>
                1,

            AccessibilityRemediationRetestResult.Inconclusive =>
                2,

            AccessibilityRemediationRetestResult.NotDetected =>
                3,

            _ =>
                4
        };
    }
}

public sealed class AccessibilityRemediationWorkflowComparisonResult
{
    public int OriginalAuditRunId { get; init; }

    public int RetestAuditRunId { get; init; }

    public string ApplicationName { get; init; }
        = string.Empty;

    public DateTime OriginalAuditStartedAt { get; init; }

    public DateTime RetestAuditStartedAt { get; init; }

    public string RetestAuditStatus { get; init; }
        = string.Empty;

    public IReadOnlyList<AccessibilityRemediationWorkflowComparisonItem>
        Items
    { get; init; }
            = Array.Empty<
                AccessibilityRemediationWorkflowComparisonItem>();

    public int TotalTracked =>
        Items.Count;

    public int StillDetected =>
        Items.Count(item =>
            item.Result ==
            AccessibilityRemediationRetestResult.Detected);

    public int NotDetected =>
        Items.Count(item =>
            item.Result ==
            AccessibilityRemediationRetestResult.NotDetected);

    public int Inconclusive =>
        Items.Count(item =>
            item.Result ==
            AccessibilityRemediationRetestResult.Inconclusive);

    public int Failed =>
        Items.Count(item =>
            item.Result ==
            AccessibilityRemediationRetestResult.Failed);
}

public sealed class AccessibilityRemediationWorkflowComparisonItem
{
    public int RemediationItemId { get; init; }

    public AccessibilityRemediationStatus CurrentStatus { get; init; }

    public string RuleId { get; init; }
        = string.Empty;

    public string FindingType { get; init; }
        = string.Empty;

    public string? Impact { get; init; }

    public int OriginalStepNumber { get; init; }

    public string? OriginalStepName { get; init; }

    public string OriginalUrl { get; init; }
        = string.Empty;

    public int? RetestStepId { get; init; }

    public int? RetestStepNumber { get; init; }

    public string? RetestStepName { get; init; }

    public string? RetestUrl { get; init; }

    public decimal? StateConfidence { get; init; }

    public AccessibilityRemediationRetestResult Result { get; init; }

    public int? MatchedAuthenticatedAuditFindingId { get; init; }

    public string MatchMethod { get; init; }
        = string.Empty;

    public decimal MatchConfidence { get; init; }

    public string? Message { get; init; }
}
