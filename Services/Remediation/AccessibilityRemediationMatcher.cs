using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace CityWebsiteAuditDashboard.Services.Remediation;

public sealed class AccessibilityRemediationMatcher
{
    private readonly ApplicationDbContext _dbContext;

    public AccessibilityRemediationMatcher(
        ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Finds the most likely matching rendered workflow state in a later
    /// authenticated audit run.
    ///
    /// For a normal single-state retest, originalAuthenticatedAuditFindingId
    /// may be null and the remediation item's earliest occurrence is used.
    ///
    /// For a formal full-workflow retest, the exact finding from the selected
    /// original audit run should be supplied.
    /// </summary>
    public async Task<AccessibilityRemediationStateMatch?>
        FindBestMatchingStepAsync(
            int remediationItemId,
            int authenticatedAuditRunId,
            CancellationToken cancellationToken = default,
            int? originalAuthenticatedAuditFindingId = null)
    {
        AccessibilityRemediationFindingOccurrence? originalOccurrence =
            await _dbContext.AccessibilityRemediationFindingOccurrences
                .AsNoTracking()
                .Where(occurrence =>
                    occurrence.AccessibilityRemediationItemId ==
                        remediationItemId &&
                    (!originalAuthenticatedAuditFindingId.HasValue ||
                     occurrence.AuthenticatedAuditFindingId ==
                        originalAuthenticatedAuditFindingId.Value))
                .OrderBy(occurrence =>
                    occurrence.LinkedAt)
                .ThenBy(occurrence =>
                    occurrence.Id)
                .Include(occurrence =>
                    occurrence.AuthenticatedAuditFinding)
                    .ThenInclude(finding =>
                        finding.AuthenticatedAuditStep)
                        .ThenInclude(step =>
                            step.AuthenticatedAuditRun)
                .FirstOrDefaultAsync(cancellationToken);

        if (originalOccurrence is null)
        {
            return null;
        }

        AuthenticatedAuditStep originalStep =
            originalOccurrence
                .AuthenticatedAuditFinding
                .AuthenticatedAuditStep;

        List<AuthenticatedAuditStep> candidateSteps =
            await _dbContext.AuthenticatedAuditSteps
                .AsNoTracking()
                .Where(step =>
                    step.AuthenticatedAuditRunId ==
                        authenticatedAuditRunId)
                .Include(step =>
                    step.AuthenticatedAuditRun)
                .ToListAsync(cancellationToken);

        var bestMatch =
            candidateSteps
                .Select(step =>
                    new
                    {
                        Step = step,

                        Confidence =
                            GetStateConfidence(
                                originalStep,
                                step)
                    })
                .Where(candidate =>
                    candidate.Confidence > 0m)
                .OrderByDescending(candidate =>
                    candidate.Confidence)

                /*
                 * Step number is only a tie-breaker.
                 * It is not treated as the workflow-state identity because
                 * workflows can gain or lose intermediate states between runs.
                 */
                .ThenBy(candidate =>
                    Math.Abs(
                        candidate.Step.StepNumber -
                        originalStep.StepNumber))
                .ThenBy(candidate =>
                    candidate.Step.StepNumber)
                .FirstOrDefault();

        if (bestMatch is null)
        {
            return null;
        }

        return new AccessibilityRemediationStateMatch
        {
            AuthenticatedAuditStepId =
                bestMatch.Step.Id,

            StepNumber =
                bestMatch.Step.StepNumber,

            StepName =
                bestMatch.Step.StepName,

            Url =
                bestMatch.Step.Url,

            StateConfidence =
                bestMatch.Confidence
        };
    }

    /// <summary>
    /// Compares one tracked accessibility finding against a newly scanned
    /// authenticated workflow state.
    ///
    /// NotDetected is returned only when the rendered state is matched with
    /// sufficient confidence and the tracked rule is absent.
    /// </summary>
    public async Task<AccessibilityRemediationMatchResult>
        MatchAsync(
            int remediationItemId,
            int authenticatedAuditStepId,
            CancellationToken cancellationToken = default,
            int? originalAuthenticatedAuditFindingId = null)
    {
        AccessibilityRemediationFindingOccurrence? originalOccurrence =
            await _dbContext.AccessibilityRemediationFindingOccurrences
                .AsNoTracking()
                .Where(occurrence =>
                    occurrence.AccessibilityRemediationItemId ==
                        remediationItemId &&
                    (!originalAuthenticatedAuditFindingId.HasValue ||
                     occurrence.AuthenticatedAuditFindingId ==
                        originalAuthenticatedAuditFindingId.Value))
                .OrderBy(occurrence =>
                    occurrence.LinkedAt)
                .ThenBy(occurrence =>
                    occurrence.Id)
                .Include(occurrence =>
                    occurrence.AuthenticatedAuditFinding)
                    .ThenInclude(finding =>
                        finding.Nodes)
                .Include(occurrence =>
                    occurrence.AuthenticatedAuditFinding)
                    .ThenInclude(finding =>
                        finding.AuthenticatedAuditStep)
                        .ThenInclude(step =>
                            step.AuthenticatedAuditRun)
                .FirstOrDefaultAsync(cancellationToken);

        if (originalOccurrence is null)
        {
            return AccessibilityRemediationMatchResult.Inconclusive(
                "OriginalFindingMissing",
                "The original tracked finding could not be loaded.");
        }

        AuthenticatedAuditStep? retestStep =
            await _dbContext.AuthenticatedAuditSteps
                .AsNoTracking()
                .Include(step =>
                    step.AuthenticatedAuditRun)
                .Include(step =>
                    step.Findings)
                    .ThenInclude(finding =>
                        finding.Nodes)
                .FirstOrDefaultAsync(
                    step =>
                        step.Id == authenticatedAuditStepId,
                    cancellationToken);

        if (retestStep is null)
        {
            throw new InvalidOperationException(
                "The authenticated audit step used for the retest could not be found.");
        }

        if (!retestStep.ScanSucceeded)
        {
            return new AccessibilityRemediationMatchResult
            {
                Result =
                    AccessibilityRemediationRetestResult.Failed,

                MatchMethod =
                    "ScanFailed",

                MatchConfidence =
                    0m,

                Message =
                    string.IsNullOrWhiteSpace(
                        retestStep.ErrorMessage)
                            ? "The accessibility retest scan did not complete successfully."
                            : "The accessibility retest scan failed: " +
                              retestStep.ErrorMessage
            };
        }

        AuthenticatedAuditFinding originalFinding =
            originalOccurrence.AuthenticatedAuditFinding;

        AuthenticatedAuditStep originalStep =
            originalFinding.AuthenticatedAuditStep;

        decimal stateConfidence =
            GetStateConfidence(
                originalStep,
                retestStep);

        if (stateConfidence == 0m)
        {
            return AccessibilityRemediationMatchResult.Inconclusive(
                "StateMismatch",
                "The scanned page or workflow state does not appear to match the original finding.");
        }

        AuthenticatedAuditFinding? matchingRule =
            retestStep.Findings
                .FirstOrDefault(finding =>
                    string.Equals(
                        finding.RuleId,
                        originalFinding.RuleId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        finding.FindingType,
                        originalFinding.FindingType,
                        StringComparison.OrdinalIgnoreCase));

        /*
         * Rule is absent.
         *
         * Do not call this NotDetected unless the underlying workflow state
         * itself matched with sufficient confidence.
         */
        if (matchingRule is null)
        {
            if (stateConfidence < 0.80m)
            {
                return AccessibilityRemediationMatchResult.Inconclusive(
                    "WeakStateMatch",
                    "The rule was not detected, but the scanned state could not be matched confidently enough to treat that as a retest pass.");
            }

            return new AccessibilityRemediationMatchResult
            {
                Result =
                    AccessibilityRemediationRetestResult.NotDetected,

                MatchMethod =
                    "RuleNotDetected",

                MatchConfidence =
                    stateConfidence,

                Message =
                    "The tracked accessibility rule was not detected in the retested state."
            };
        }

        /*
         * Strongest finding-level match:
         * same axe rule and one of the same affected targets.
         */
        HashSet<string> originalTargets =
            originalFinding.Nodes
                .Select(node =>
                    NormalizeTarget(node.Target))
                .Where(target =>
                    !string.IsNullOrWhiteSpace(target))
                .ToHashSet(
                    StringComparer.Ordinal);

        HashSet<string> retestTargets =
            matchingRule.Nodes
                .Select(node =>
                    NormalizeTarget(node.Target))
                .Where(target =>
                    !string.IsNullOrWhiteSpace(target))
                .ToHashSet(
                    StringComparer.Ordinal);

        bool targetMatched =
            originalTargets.Overlaps(
                retestTargets);

        if (targetMatched)
        {
            return new AccessibilityRemediationMatchResult
            {
                Result =
                    AccessibilityRemediationRetestResult.Detected,

                MatchedAuthenticatedAuditFindingId =
                    matchingRule.Id,

                MatchMethod =
                    "RuleAndTarget",

                MatchConfidence =
                    Math.Min(
                        1.0000m,
                        stateConfidence),

                Message =
                    "The same rule was detected again on at least one of the original affected targets."
            };
        }

        /*
         * Second-strongest finding-level match:
         * same axe rule and equivalent saved element HTML.
         */
        HashSet<string> originalHtml =
            originalFinding.Nodes
                .Select(node =>
                    NormalizeHtml(node.Html))
                .Where(html =>
                    !string.IsNullOrWhiteSpace(html))
                .ToHashSet(
                    StringComparer.Ordinal);

        HashSet<string> retestHtml =
            matchingRule.Nodes
                .Select(node =>
                    NormalizeHtml(node.Html))
                .Where(html =>
                    !string.IsNullOrWhiteSpace(html))
                .ToHashSet(
                    StringComparer.Ordinal);

        bool htmlMatched =
            originalHtml.Overlaps(
                retestHtml);

        if (htmlMatched)
        {
            return new AccessibilityRemediationMatchResult
            {
                Result =
                    AccessibilityRemediationRetestResult.Detected,

                MatchedAuthenticatedAuditFindingId =
                    matchingRule.Id,

                MatchMethod =
                    "RuleAndHtml",

                MatchConfidence =
                    Math.Min(
                        0.9000m,
                        stateConfidence),

                Message =
                    "The same accessibility rule and affected HTML were detected again."
            };
        }

        /*
         * A remediation item represents one rule-level finding for one
         * rendered workflow state.
         *
         * If the state matched strongly and the same rule remains, the issue
         * is still considered detected even if the exact affected node changed.
         */
        if (stateConfidence >= 0.80m)
        {
            return new AccessibilityRemediationMatchResult
            {
                Result =
                    AccessibilityRemediationRetestResult.Detected,

                MatchedAuthenticatedAuditFindingId =
                    matchingRule.Id,

                MatchMethod =
                    "RuleOnly",

                MatchConfidence =
                    Math.Min(
                        0.8000m,
                        stateConfidence),

                Message =
                    "The same accessibility rule is still present, although the exact affected element changed."
            };
        }

        return AccessibilityRemediationMatchResult.Inconclusive(
            "WeakRuleMatch",
            "The rule was detected, but the rendered state and affected elements could not be matched confidently.");
    }

    /// <summary>
    /// Scores how confidently two authenticated audit steps represent the same
    /// rendered page or workflow state.
    /// </summary>
    private static decimal GetStateConfidence(
        AuthenticatedAuditStep originalStep,
        AuthenticatedAuditStep retestStep)
    {
        string originalApplication =
            originalStep
                .AuthenticatedAuditRun
                .ApplicationName;

        string retestApplication =
            retestStep
                .AuthenticatedAuditRun
                .ApplicationName;

        if (!string.Equals(
            originalApplication.Trim(),
            retestApplication.Trim(),
            StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        /*
         * Exact DOM fingerprint is the strongest state evidence.
         * It is not required because a legitimate accessibility fix may
         * intentionally change the DOM.
         */
        if (!string.IsNullOrWhiteSpace(
                originalStep.DomFingerprint) &&
            string.Equals(
                originalStep.DomFingerprint,
                retestStep.DomFingerprint,
                StringComparison.Ordinal))
        {
            return 1.0000m;
        }

        bool sameUrl =
            string.Equals(
                NormalizeUrl(originalStep.Url),
                NormalizeUrl(retestStep.Url),
                StringComparison.OrdinalIgnoreCase);

        if (!sameUrl)
        {
            return 0m;
        }

        if (!string.IsNullOrWhiteSpace(
                originalStep.Heading) &&
            string.Equals(
                originalStep.Heading.Trim(),
                retestStep.Heading?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return 0.9000m;
        }

        if (!string.IsNullOrWhiteSpace(
                originalStep.PageTitle) &&
            string.Equals(
                originalStep.PageTitle.Trim(),
                retestStep.PageTitle?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return 0.8500m;
        }

        /*
         * Same application + same normalized URL is useful evidence,
         * but not enough by itself to declare a missing rule fixed.
         */
        return 0.7000m;
    }

    private static string NormalizeUrl(
        string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(
            url,
            UriKind.Absolute,
            out Uri? parsedUrl))
        {
            return url.Trim();
        }

        string authority =
            parsedUrl.GetLeftPart(
                UriPartial.Authority);

        string path =
            parsedUrl.AbsolutePath
                .TrimEnd('/');

        return authority + path;
    }

    private static string NormalizeTarget(
        string? target)
    {
        return target?.Trim() ??
               string.Empty;
    }

    private static string NormalizeHtml(
        string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return Regex.Replace(
            html.Trim(),
            @"\s+",
            " ");
    }
}

public sealed class AccessibilityRemediationMatchResult
{
    public AccessibilityRemediationRetestResult Result { get; init; }

    public int? MatchedAuthenticatedAuditFindingId { get; init; }

    public string MatchMethod { get; init; }
        = string.Empty;

    public decimal MatchConfidence { get; init; }

    public string? Message { get; init; }

    public static AccessibilityRemediationMatchResult Inconclusive(
        string matchMethod,
        string message)
    {
        return new AccessibilityRemediationMatchResult
        {
            Result =
                AccessibilityRemediationRetestResult.Inconclusive,

            MatchMethod =
                matchMethod,

            MatchConfidence =
                0m,

            Message =
                message
        };
    }
}

public sealed class AccessibilityRemediationStateMatch
{
    public int AuthenticatedAuditStepId { get; init; }

    public int StepNumber { get; init; }

    public string? StepName { get; init; }

    public string Url { get; init; }
        = string.Empty;

    public decimal StateConfidence { get; init; }
}
