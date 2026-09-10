using CityWebsiteAuditDashboard.Models;

namespace CityWebsiteAuditDashboard.ViewModels;

public sealed class AccessibilityWorkflowRetestViewModel
{
    public int? OriginalAuditRunId
    {
        get;
        set;
    }


    public int? RetestAuditRunId
    {
        get;
        set;
    }


    public IReadOnlyList<
        AccessibilityWorkflowRetestRunOptionViewModel>
        AuditRuns
    {
        get;
        init;
    } =
        Array.Empty<
            AccessibilityWorkflowRetestRunOptionViewModel>();


    public AccessibilityWorkflowRetestComparisonViewModel?
        Comparison
    {
        get;
        init;
    }


    public IReadOnlyList<
        AccessibilityWorkflowRetestRunOptionViewModel>
        EligibleRetestRuns
    {
        get;
        init;
    } =
        Array.Empty<
            AccessibilityWorkflowRetestRunOptionViewModel>();


    public IReadOnlyList<
        AccessibilityWorkflowRetestHistoryViewModel>
        FormalWorkflowRetests
    {
        get;
        init;
    } =
        Array.Empty<
            AccessibilityWorkflowRetestHistoryViewModel>();
}


public sealed class AccessibilityWorkflowRetestRunOptionViewModel
{
    public int Id
    {
        get;
        init;
    }


    public string ApplicationName
    {
        get;
        init;
    } = string.Empty;


    public DateTime StartedAt
    {
        get;
        init;
    }


    public string Status
    {
        get;
        init;
    } = string.Empty;


    public int StepCount
    {
        get;
        init;
    }
}


/*
 * Read-only comparison shown before a formal remediation retest
 * is recorded.
 *
 * The comparison now represents ALL saved accessibility findings
 * from the original audit, not only findings already in the
 * Remediation Tracker.
 */
public sealed class AccessibilityWorkflowRetestComparisonViewModel
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


    public bool FormalRetestAlreadyApplied
    {
        get;
        init;
    }


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


    /*
     * Workflow-state counts are displayed for context only.
     *
     * They are NOT required to match.
     */
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
     * Full audit-comparison metrics.
     */
    public int TotalCompared
    {
        get;
        init;
    }


    public int StillDetected
    {
        get;
        init;
    }


    public int NotDetected
    {
        get;
        init;
    }


    public int Inconclusive
    {
        get;
        init;
    }


    public int Failed
    {
        get;
        init;
    }


    /*
     * Tracking information is kept separate from the read-only
     * audit comparison.
     *
     * A formal remediation retest can only be applied to the
     * tracked subset.
     */
    public int TotalTracked
    {
        get;
        init;
    }


    public int UntrackedFindingCount
    {
        get;
        init;
    }


    public bool HasTrackedFindings =>
        TotalTracked > 0;


    public bool HasUntrackedFindings =>
        UntrackedFindingCount > 0;


    public IReadOnlyList<
        AccessibilityWorkflowRetestComparisonItemViewModel>
        Items
    {
        get;
        init;
    } =
        Array.Empty<
            AccessibilityWorkflowRetestComparisonItemViewModel>();
}


/*
 * One finding in the read-only audit-to-audit comparison.
 *
 * RemediationItemId and CurrentStatus are nullable because a finding
 * does not need to be in the Remediation Tracker merely to be
 * compared with a later audit.
 */
public sealed class AccessibilityWorkflowRetestComparisonItemViewModel
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


public sealed class AccessibilityWorkflowRetestHistoryViewModel
{
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


    public DateTime RetestedAt
    {
        get;
        init;
    }


    public int TotalTracked
    {
        get;
        init;
    }


    public int StillDetected
    {
        get;
        init;
    }


    public int NotDetected
    {
        get;
        init;
    }


    public int VerifiedFromThisRetest
    {
        get;
        init;
    }


    public string VerificationState =>
        NotDetected == 0 ||
        VerifiedFromThisRetest == 0
            ? "Applied"
            : VerifiedFromThisRetest <
              NotDetected
                ? "Partially Verified"
                : "Fully Verified";


    public int Inconclusive
    {
        get;
        init;
    }


    public int Failed
    {
        get;
        init;
    }


    public int Reopened
    {
        get;
        init;
    }
}


public sealed class AccessibilityWorkflowRetestDetailsViewModel
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


    public DateTime RetestedAt
    {
        get;
        init;
    }


    public int TotalTracked
    {
        get;
        init;
    }


    public int StillDetected
    {
        get;
        init;
    }


    public int NotDetected
    {
        get;
        init;
    }


    public int VerifiedFromThisRetest
    {
        get;
        init;
    }


    public string VerificationState =>
        NotDetected == 0 ||
        VerifiedFromThisRetest == 0
            ? "Applied"
            : VerifiedFromThisRetest <
              NotDetected
                ? "Partially Verified"
                : "Fully Verified";


    public int Inconclusive
    {
        get;
        init;
    }


    public int Failed
    {
        get;
        init;
    }


    public int Reopened
    {
        get;
        init;
    }


    public IReadOnlyList<
        AccessibilityWorkflowRetestSavedItemViewModel>
        Items
    {
        get;
        init;
    } =
        Array.Empty<
            AccessibilityWorkflowRetestSavedItemViewModel>();
}


public sealed class AccessibilityWorkflowRetestSavedItemViewModel
{
    public int RemediationItemId
    {
        get;
        init;
    }


    public int? OriginalAuthenticatedAuditFindingId
    {
        get;
        init;
    }


    public int? MatchedAuthenticatedAuditFindingId
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


    public AccessibilityRemediationRetestResult Result
    {
        get;
        init;
    }


    public string? MatchMethod
    {
        get;
        init;
    }


    public decimal? MatchConfidence
    {
        get;
        init;
    }


    public string? Notes
    {
        get;
        init;
    }


    public DateTime RetestedAt
    {
        get;
        init;
    }


    public bool CanVerify
    {
        get;
        init;
    }


    public bool WasReopened
    {
        get;
        init;
    }


    public bool VerifiedFromThisRetest
    {
        get;
        init;
    }
}
