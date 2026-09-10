namespace CityWebsiteAuditDashboard.ViewModels;

/// <summary>
/// Contains the authenticated audit runs displayed on the history page,
/// together with the current history filters.
/// </summary>
public sealed class AuthenticatedAuditHistoryViewModel
{
    public IReadOnlyList<AuthenticatedAuditRunSummaryViewModel>
        Runs
    {
        get;
        init;
    } =
        Array.Empty<
            AuthenticatedAuditRunSummaryViewModel>();


    /*
     * Current filters.
     */
    public string? ApplicationFilter
    {
        get;
        init;
    }


    public string? StatusFilter
    {
        get;
        init;
    }


    public DateTime? StartDate
    {
        get;
        init;
    }


    public DateTime? EndDate
    {
        get;
        init;
    }


    /*
     * Filter dropdown choices.
     */
    public IReadOnlyList<string> ApplicationOptions
    {
        get;
        init;
    } =
        Array.Empty<string>();


    public IReadOnlyList<string> StatusOptions
    {
        get;
        init;
    } =
        Array.Empty<string>();


    /*
     * Number of audit runs before filters are applied.
     *
     * This lets the page show:
     *
     *     Showing 8 of 24 audit runs
     */
    public int TotalAvailableRuns
    {
        get;
        init;
    }


    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(
            ApplicationFilter) ||

        !string.IsNullOrWhiteSpace(
            StatusFilter) ||

        StartDate.HasValue ||

        EndDate.HasValue;
}


/// <summary>
/// Summary information for one authenticated audit run.
///
/// Detailed step results are displayed on the separate audit details page.
/// </summary>
public sealed class AuthenticatedAuditRunSummaryViewModel
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


    public string StartingUrl
    {
        get;
        init;
    } = string.Empty;


    public string AccessibilityEngine
    {
        get;
        init;
    } = string.Empty;


    public DateTime StartedAt
    {
        get;
        init;
    }


    public DateTime? CompletedAt
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


    public int SuccessfulStepCount
    {
        get;
        init;
    }


    public int FailedStepCount
    {
        get;
        init;
    }


    public int? FinalStepNumber
    {
        get;
        init;
    }


    public string? ErrorMessage
    {
        get;
        init;
    }
}
