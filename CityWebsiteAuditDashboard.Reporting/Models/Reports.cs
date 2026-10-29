using System.Globalization;

namespace CityWebsiteAuditDashboard.Reporting.Models;

public static class ReportDisplay
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Pacific Standard Time" : "America/Los_Angeles");
    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone));
    public static string Time(DateTime value) => TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.SpecifyKind(value, DateTimeKind.Utc), Zone).ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
    public static string Number(long value) => value.ToString("N0", CultureInfo.GetCultureInfo("en-US"));
    public static string Status(string value) => value switch
    {
        "InProgress" => "In progress",
        "WontFix" => "Won’t fix",
        "NeedsReview" => "Needs manual review",
        "NotDetected" => "Not detected",
        _ => value
    };
    public static string Tone(string value) => value.ToLowerInvariant() switch
    {
        "critical" or "serious" or "violation" or "failed" or "open" or "detected" => "danger",
        "moderate" or "needsreview" or "needs manual review" or "inprogress" or "in progress" or "running" or "interrupted" or "inconclusive" => "warning",
        "minor" or "fixed" or "verified" or "completed" or "succeeded" or "notdetected" => "success",
        _ => "neutral"
    };
    public static string Priority(string impact) => impact.ToLowerInvariant() switch
    { "critical" or "serious" => "Fix first", "moderate" => "Plan next", "minor" => "Lower priority", _ => "Review priority" };
    public static string? SafeLink(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
}

public sealed class ReportFilter
{
    public string? Application { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public int Page { get; set; } = 1;
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public string? Resolve()
    {
        Application = string.IsNullOrWhiteSpace(Application) ? null : Application.Trim();
        From ??= ReportDisplay.Today.AddDays(-89).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        To ??= ReportDisplay.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (Application?.Length > 200) return "Application names must be 200 characters or fewer.";
        if (!DateOnly.TryParseExact(From, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateOnly.TryParseExact(To, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            return "Enter valid start and end dates.";
        if (from.Year < 1900 || from > to || to.Year >= 9999 || to.DayNumber - from.DayNumber > 3660)
            return "Choose an end date on or after the start date, with a range of 10 years or less.";
        if (Page < 1 || Page > 100000) return "Choose a valid page number.";
        StartUtc = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), ReportDisplay.Zone);
        EndUtc = TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), ReportDisplay.Zone);
        return null;
    }
    public Dictionary<string, string> Routes(int? page = null) => new()
    {
        ["Application"] = Application ?? "",
        ["From"] = From ?? "",
        ["To"] = To ?? "",
        ["Page"] = (page ?? Page).ToString(CultureInfo.InvariantCulture)
    };
}

public sealed record CountRow(string Label, long Count);
public sealed record AuditRow(int Id, string Application, DateTime Started, string Status,
    long Steps, long SuccessfulSteps, long Violations, long Review, long Elements);
public sealed record FindingRow(int Id, int Step, string StepName, string Rule, string Type,
    string Impact, string Help, string Description, string? HelpUrl, string? Level, int Elements)
{
    public string Tags { get; init; } = "";
    public List<ElementRow> Nodes { get; set; } = [];
}
public sealed record ElementRow(int FindingId, string Target, string? Html, string? FailureSummary, string? Guidance);
public sealed record StepRow(int Number, string Name, DateTime Scanned, bool Succeeded,
    int Violations, int Review, int Elements);
public sealed record RemediationRow(int Id, string Application, string Rule, string Help,
    string Impact, string Status, string? Assignee, DateTime Updated, int RunId,
    string? Retest, DateTime? Retested, string? Notes, string? HelpUrl);

public sealed class ReportPage
{
    public ReportFilter Filter { get; set; } = new();
    public List<string> Applications { get; set; } = [];
    public string? ValidationError { get; set; }
    public List<CountRow> Severity { get; set; } = [];
    public List<CountRow> Statuses { get; set; } = [];
    public List<CountRow> Activity { get; set; } = [];
    public List<AuditRow> Audits { get; set; } = [];
    public List<FindingRow> Findings { get; set; } = [];
    public List<StepRow> Steps { get; set; } = [];
    public List<RemediationRow> Items { get; set; } = [];
    public AuditRow? Audit { get; set; }
    public string? AuditNotes { get; set; }
    public long Total { get; set; }
    public long ApplicationsCount { get; set; }
    public long CompletedCount { get; set; }
    public long OtherCount { get; set; }
    public long ReviewCount { get; set; }
    public long ViolationCount => Severity.Sum(x => x.Count);
    public long PageCount => Math.Max(1, (Total + 24) / 25);
    public bool HasNext => (long)Filter.Page * 25 < Total;
}

