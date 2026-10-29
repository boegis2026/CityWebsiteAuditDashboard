using System.Data;
using Microsoft.Data.SqlClient;
using CityWebsiteAuditDashboard.Reporting.Models;

namespace CityWebsiteAuditDashboard.Reporting.Data;

// Parameterized SELECT statements only. SQL permissions are the actual write boundary.
public sealed class ReportStore(IConfiguration configuration)
{
    private readonly string connection = configuration.GetConnectionString("ReportingDatabase")!;
    private const string Runs = """
        WITH Filtered AS (
            SELECT Id, ApplicationName, StartedAt, Status FROM dbo.AuthenticatedAuditRuns
            WHERE (@app IS NULL OR ApplicationName = @app)
              AND StartedAt >= @from AND StartedAt < @to
        ), Ranked AS (
            SELECT *, ROW_NUMBER() OVER(PARTITION BY ApplicationName ORDER BY StartedAt DESC, Id DESC) AS rn
            FROM Filtered WHERE Status = N'Completed'
        ), Snapshot AS (SELECT * FROM Ranked WHERE rn = 1)
        """;
    private const string Items = """
        , Linked AS (
            SELECT o.AccessibilityRemediationItemId AS ItemId, r.ApplicationName,
                   f.RuleId, f.Help, f.Impact, f.HelpUrl, r.Id AS RunId,
                   ROW_NUMBER() OVER(PARTITION BY o.AccessibilityRemediationItemId
                       ORDER BY r.StartedAt DESC, r.Id DESC, s.StepNumber DESC, f.Id DESC) AS rn
            FROM dbo.AccessibilityRemediationFindingOccurrences o
            JOIN dbo.AuthenticatedAuditFindings f ON f.Id = o.AuthenticatedAuditFindingId
            JOIN dbo.AuthenticatedAuditSteps s ON s.Id = f.AuthenticatedAuditStepId
            JOIN Filtered r ON r.Id = s.AuthenticatedAuditRunId
        )
        """;
    private const string AuditColumns = """
        SELECT r.Id, r.ApplicationName, r.StartedAt, r.Status,
               st.TotalSteps, st.SuccessfulSteps, ft.Violations, ft.Review, ft.Elements
        FROM Filtered r
        OUTER APPLY (
            SELECT COUNT_BIG(*) AS TotalSteps,
                   COALESCE(SUM(CAST(s.ScanSucceeded AS bigint)), 0) AS SuccessfulSteps
            FROM dbo.AuthenticatedAuditSteps s WHERE s.AuthenticatedAuditRunId = r.Id
        ) st
        OUTER APPLY (
            SELECT COALESCE(SUM(CASE WHEN f.FindingType = N'Violation' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS Violations,
                   COALESCE(SUM(CASE WHEN f.FindingType = N'NeedsReview' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS Review,
                   COALESCE(SUM(CASE WHEN f.FindingType = N'Violation' THEN CAST(f.AffectedElementCount AS bigint) ELSE 0 END), 0) AS Elements
            FROM dbo.AuthenticatedAuditSteps s
            JOIN dbo.AuthenticatedAuditFindings f ON f.AuthenticatedAuditStepId = s.Id
            WHERE s.AuthenticatedAuditRunId = r.Id AND s.ScanSucceeded = 1
        ) ft
        """;

    private static string Text(SqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
    private static string? Optional(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static long Long(SqlDataReader r, int i) => Convert.ToInt64(r.GetValue(i));
    private static AuditRow Audit(SqlDataReader r) => new(r.GetInt32(0), Text(r, 1), r.GetDateTime(2),
        Text(r, 3), Long(r, 4), Long(r, 5), Long(r, 6), Long(r, 7), Long(r, 8));

    private async Task<List<T>> Read<T>(string sql, ReportFilter filter,
        Func<SqlDataReader, T> map, CancellationToken token, int? id = null)
    {
        await using var db = new SqlConnection(connection);
        await db.OpenAsync(token);
        await using var command = new SqlCommand(sql, db) { CommandTimeout = 30 };
        command.Parameters.Add("@app", SqlDbType.NVarChar, 200).Value = (object?)filter.Application ?? DBNull.Value;
        command.Parameters.Add("@from", SqlDbType.DateTime2).Value = filter.StartUtc;
        command.Parameters.Add("@to", SqlDbType.DateTime2).Value = filter.EndUtc;
        command.Parameters.Add("@offset", SqlDbType.Int).Value = (filter.Page - 1) * 25;
        command.Parameters.Add("@id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new List<T>();
        while (await reader.ReadAsync(token)) result.Add(map(reader));
        return result;
    }

    public Task<List<string>> Applications(ReportFilter f, CancellationToken ct) => Read(
        "SELECT DISTINCT ApplicationName FROM dbo.AuthenticatedAuditRuns ORDER BY ApplicationName;",
        f, r => Text(r, 0), ct);

    public async Task Overview(ReportPage page, CancellationToken ct)
    {
        var f = page.Filter;
        var counts = (await Read(Runs + """
            SELECT (SELECT COUNT_BIG(*) FROM Snapshot),
                   (SELECT COUNT_BIG(*) FROM Filtered WHERE Status = N'Completed'),
                   (SELECT COUNT_BIG(*) FROM Filtered WHERE Status <> N'Completed'),
                   (SELECT COUNT_BIG(*) FROM Snapshot r
                    JOIN dbo.AuthenticatedAuditSteps s ON s.AuthenticatedAuditRunId = r.Id
                    JOIN dbo.AuthenticatedAuditFindings f ON f.AuthenticatedAuditStepId = s.Id
                    WHERE s.ScanSucceeded = 1 AND f.FindingType = N'NeedsReview');
            """, f, r => new[] { Long(r, 0), Long(r, 1), Long(r, 2), Long(r, 3) }, ct)).Single();
        page.ApplicationsCount = counts[0]; page.CompletedCount = counts[1];
        page.OtherCount = counts[2]; page.ReviewCount = counts[3];
        var severity = await Read(Runs + """
            SELECT LOWER(COALESCE(f.Impact, N'unknown')), COUNT_BIG(*)
            FROM Snapshot r
            JOIN dbo.AuthenticatedAuditSteps s ON s.AuthenticatedAuditRunId = r.Id
            JOIN dbo.AuthenticatedAuditFindings f ON f.AuthenticatedAuditStepId = s.Id
            WHERE s.ScanSucceeded = 1 AND f.FindingType = N'Violation'
            GROUP BY LOWER(COALESCE(f.Impact, N'unknown'));
            """, f, r => new CountRow(Text(r, 0), Long(r, 1)), ct);
        page.Severity = new[] { "critical", "serious", "moderate", "minor" }
            .Select(label => new CountRow(label, severity.Where(x => x.Label == label).Sum(x => x.Count))).ToList();
        page.Severity.Add(new("unknown", severity.Where(x => !new[] { "critical", "serious", "moderate", "minor" }
            .Contains(x.Label)).Sum(x => x.Count)));
        page.Statuses = await Read(Runs + Items + """
            SELECT i.Status, COUNT_BIG(*) FROM dbo.AccessibilityRemediationItems i
            JOIN Linked l ON l.ItemId = i.Id AND l.rn = 1
            GROUP BY i.Status ORDER BY CASE i.Status
                WHEN N'Open' THEN 0 WHEN N'InProgress' THEN 1 WHEN N'Fixed' THEN 2
                WHEN N'Verified' THEN 3 WHEN N'WontFix' THEN 4 ELSE 5 END;
            """, f, r => new CountRow(ReportDisplay.Status(Text(r, 0)), Long(r, 1)), ct);
        page.Activity = await Read(Runs + """
            SELECT CONVERT(char(7), StartedAt AT TIME ZONE 'UTC' AT TIME ZONE 'Pacific Standard Time', 126), COUNT_BIG(*)
            FROM Filtered WHERE Status = N'Completed'
            GROUP BY CONVERT(char(7), StartedAt AT TIME ZONE 'UTC' AT TIME ZONE 'Pacific Standard Time', 126)
            ORDER BY 1;
            """, f, r => new CountRow(Text(r, 0), Long(r, 1)), ct);
        page.Audits = await Read(Runs + AuditColumns +
            " WHERE r.Id IN (SELECT Id FROM Snapshot) ORDER BY r.StartedAt DESC, r.Id DESC OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY;", f, Audit, ct);
    }

    public async Task History(ReportPage page, CancellationToken ct)
    {
        page.Total = (await Read(Runs + "SELECT COUNT_BIG(*) FROM Filtered;", page.Filter, r => Long(r, 0), ct)).Single();
        page.Audits = await Read(Runs + AuditColumns +
            " ORDER BY r.StartedAt DESC, r.Id DESC OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY;", page.Filter, Audit, ct);
    }

    public async Task Details(ReportPage page, int id, CancellationToken ct, bool all = false)
    {
        const string one = "WITH Filtered AS (SELECT Id, ApplicationName, StartedAt, Status FROM dbo.AuthenticatedAuditRuns WHERE Id = @id) ";
        page.Audit = (await Read(one + AuditColumns, page.Filter, Audit, ct, id)).SingleOrDefault();
        if (page.Audit is null) return;
        page.AuditNotes = (await Read("SELECT AuditNotes FROM dbo.AuthenticatedAuditRuns WHERE Id = @id;",
            page.Filter, r => Optional(r, 0), ct, id)).SingleOrDefault();
        page.Steps = await Read("""
            SELECT StepNumber, StepName, ScannedAt, ScanSucceeded, ViolationRuleCount, NeedsReviewRuleCount, AffectedElementCount
            FROM dbo.AuthenticatedAuditSteps WHERE AuthenticatedAuditRunId = @id ORDER BY StepNumber;
            """, page.Filter, r => new StepRow(r.GetInt32(0), Text(r, 1), r.GetDateTime(2), r.GetBoolean(3),
                r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)), ct, id);
        page.Total = (await Read("""
            SELECT COUNT_BIG(*) FROM dbo.AuthenticatedAuditFindings f
            JOIN dbo.AuthenticatedAuditSteps s ON s.Id = f.AuthenticatedAuditStepId
            WHERE s.AuthenticatedAuditRunId = @id AND s.ScanSucceeded = 1;
            """, page.Filter, r => Long(r, 0), ct, id)).Single();
        page.Findings = await Read("""
            SELECT f.Id, s.StepNumber, s.StepName, f.RuleId, f.FindingType, COALESCE(f.Impact, N'unknown'),
                   f.Help, f.Description, f.HelpUrl, f.WcagLevel, f.AffectedElementCount, f.WcagTags
            FROM dbo.AuthenticatedAuditFindings f
            JOIN dbo.AuthenticatedAuditSteps s ON s.Id = f.AuthenticatedAuditStepId
            WHERE s.AuthenticatedAuditRunId = @id AND s.ScanSucceeded = 1
            ORDER BY CASE f.FindingType WHEN N'Violation' THEN 0 ELSE 1 END,
                     CASE f.Impact WHEN N'critical' THEN 0 WHEN N'serious' THEN 1 WHEN N'moderate' THEN 2 WHEN N'minor' THEN 3 ELSE 4 END,
                     s.StepNumber, f.Id
            """ + (all ? ";" : " OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY;"), page.Filter, r => new FindingRow(r.GetInt32(0), r.GetInt32(1), Text(r, 2), Text(r, 3), Text(r, 4),
                Text(r, 5), Text(r, 6), Text(r, 7), Optional(r, 8), Optional(r, 9), r.GetInt32(10))
        { Tags = Text(r, 11) }, ct, id);
        if (page.Findings.Count > 0)
        {
            // IDs originate from the preceding database query, never from request text.
            var ids = string.Join(",", page.Findings.Select(x => x.Id));
            var predicate = all ? "AuthenticatedAuditFindingId IN (SELECT f.Id FROM dbo.AuthenticatedAuditFindings f JOIN dbo.AuthenticatedAuditSteps s ON s.Id=f.AuthenticatedAuditStepId WHERE s.AuthenticatedAuditRunId=@id AND s.ScanSucceeded=1)" : $"AuthenticatedAuditFindingId IN ({ids})";
            var nodes = await Read($"""
                SELECT AuthenticatedAuditFindingId, Target, Html, FailureSummary, ElementFixGuidance
                FROM dbo.AuthenticatedAuditFindingNodes
                WHERE {predicate} ORDER BY AuthenticatedAuditFindingId, Id;
                """, page.Filter, r => new ElementRow(r.GetInt32(0), Text(r, 1), Optional(r, 2), Optional(r, 3), Optional(r, 4)), ct, id);
            var lookup = nodes.ToLookup(x => x.FindingId);
            foreach (var finding in page.Findings) finding.Nodes = lookup[finding.Id].ToList();
        }
    }

    public async Task Remediation(ReportPage page, CancellationToken ct)
    {
        page.Total = (await Read(Runs + Items + "SELECT COUNT_BIG(*) FROM Linked WHERE rn = 1;",
            page.Filter, r => Long(r, 0), ct)).Single();
        page.Items = await Read(Runs + Items + """
            SELECT i.Id, l.ApplicationName, l.RuleId, l.Help, COALESCE(l.Impact, N'unknown'), i.Status,
                   i.AssignedTo, i.UpdatedAt, l.RunId, t.Result, t.RetestedAt, h.Notes, l.HelpUrl
            FROM dbo.AccessibilityRemediationItems i
            JOIN Linked l ON l.ItemId = i.Id AND l.rn = 1
            OUTER APPLY (SELECT TOP (1) Result, RetestedAt FROM dbo.AccessibilityRemediationRetests
                         WHERE AccessibilityRemediationItemId = i.Id ORDER BY RetestedAt DESC, Id DESC) t
            OUTER APPLY (SELECT TOP (1) Notes FROM dbo.AccessibilityRemediationHistories
                         WHERE AccessibilityRemediationItemId = i.Id AND NULLIF(LTRIM(RTRIM(Notes)), N'') IS NOT NULL
                         ORDER BY ChangedAt DESC, Id DESC) h
            ORDER BY CASE i.Status WHEN N'Open' THEN 0 WHEN N'InProgress' THEN 1 WHEN N'Fixed' THEN 2 ELSE 3 END,
                     i.UpdatedAt DESC, i.Id DESC OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY;
            """, page.Filter, r => new RemediationRow(r.GetInt32(0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4),
                Text(r, 5), Optional(r, 6), r.GetDateTime(7), r.GetInt32(8), Optional(r, 9),
                r.IsDBNull(10) ? null : r.GetDateTime(10), Optional(r, 11), Optional(r, 12)), ct);
    }
}

