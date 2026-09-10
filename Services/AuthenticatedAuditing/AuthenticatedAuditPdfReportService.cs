using CityWebsiteAuditDashboard.ViewModels;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

public sealed class AuthenticatedAuditPdfReportService
    : IAuthenticatedAuditPdfReportService
{
    private static readonly Color Navy =
        Color.FromRgb(18, 52, 77);

    private static readonly Color Blue =
        Color.FromRgb(23, 105, 166);

    private static readonly Color LightBlue =
        Color.FromRgb(234, 243, 249);

    private static readonly Color BorderColor =
        Color.FromRgb(217, 224, 230);

    private static readonly Color LightGray =
        Color.FromRgb(244, 246, 248);

    private static readonly Color DarkText =
        Color.FromRgb(31, 41, 51);

    private static readonly Color MutedText =
        Color.FromRgb(86, 101, 114);

    private static readonly Color Danger =
        Color.FromRgb(176, 37, 44);

    private static readonly Color Warning =
        Color.FromRgb(151, 92, 0);

    private static readonly Color Success =
        Color.FromRgb(24, 112, 57);


    public byte[] CreatePdf(
        AuthenticatedAuditDetailsViewModel audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        Document document = new();

        document.Info.Title =
            $"{audit.ApplicationName} Accessibility Audit";

        document.Info.Subject =
            "Authenticated accessibility audit report";

        document.Info.Author =
            "City of Los Angeles - Bureau of Engineering";

        ConfigureStyles(document);


        List<AuthenticatedAuditFindingDetailsViewModel> allFindings =
            audit.Steps
                .SelectMany(step => step.Findings)
                .ToList();


        int violationFindingCount =
            allFindings.Count(finding =>
                string.Equals(
                    finding.FindingType,
                    "Violation",
                    StringComparison.OrdinalIgnoreCase));


        int needsReviewFindingCount =
            allFindings.Count(finding =>
                string.Equals(
                    finding.FindingType,
                    "NeedsReview",
                    StringComparison.OrdinalIgnoreCase));


        int criticalFindingCount =
            allFindings.Count(finding =>
                string.Equals(
                    finding.Impact,
                    "critical",
                    StringComparison.OrdinalIgnoreCase));


        int seriousFindingCount =
            allFindings.Count(finding =>
                string.Equals(
                    finding.Impact,
                    "serious",
                    StringComparison.OrdinalIgnoreCase));


        int moderateFindingCount =
            allFindings.Count(finding =>
                string.Equals(
                    finding.Impact,
                    "moderate",
                    StringComparison.OrdinalIgnoreCase));


        int minorFindingCount =
            allFindings.Count(finding =>
                string.Equals(
                    finding.Impact,
                    "minor",
                    StringComparison.OrdinalIgnoreCase));


        int wcagLevelACount =
            allFindings.Count(finding =>
                string.Equals(
                    finding.WcagLevel,
                    "A",
                    StringComparison.OrdinalIgnoreCase));


        int wcagLevelAaCount =
            allFindings.Count(finding =>
                string.Equals(
                    finding.WcagLevel,
                    "AA",
                    StringComparison.OrdinalIgnoreCase));


        int otherWcagCount =
            allFindings.Count(finding =>
                string.IsNullOrWhiteSpace(
                    finding.WcagLevel));


        int fixFirstFindingCount =
            allFindings.Count(IsFixFirstFinding);


        var remediationItems =
            allFindings
                .Where(finding =>
                    finding.RemediationItemId.HasValue)
                .GroupBy(finding =>
                    finding.RemediationItemId!.Value)
                .Select(group => new
                {
                    Id = group.Key,

                    Status =
                        group
                            .Select(finding =>
                                finding.RemediationStatus)
                            .FirstOrDefault(status =>
                                !string.IsNullOrWhiteSpace(
                                    status))
                })
                .ToList();


        int remediationOpen =
            remediationItems.Count(item =>
                NormalizeRemediationStatus(
                    item.Status) == "open");


        int remediationInProgress =
            remediationItems.Count(item =>
                NormalizeRemediationStatus(
                    item.Status) == "inprogress");


        int remediationAwaitingVerification =
            remediationItems.Count(item =>
                NormalizeRemediationStatus(
                    item.Status) == "fixed");


        int remediationVerified =
            remediationItems.Count(item =>
                NormalizeRemediationStatus(
                    item.Status) == "verified");


        int remediationWontFix =
            remediationItems.Count(item =>
                NormalizeRemediationStatus(
                    item.Status) == "wontfix");


        Section section =
            document.AddSection();

        ConfigurePage(section);

        AddFooter(section);


        // =====================================================
        // PAGE 1 - Executive summary
        // =====================================================

        AddReportBanner(
            section,
            audit);

        AddReportMetadata(
            section,
            audit);


        AddSectionHeading(
            section,
            "Executive Summary");

        AddExecutiveSummary(
            section,
            audit,
            violationFindingCount,
            needsReviewFindingCount,
            fixFirstFindingCount,
            criticalFindingCount,
            seriousFindingCount);


        AddSectionHeading(
            section,
            "Finding Priority");

        AddSeveritySummary(
            section,
            criticalFindingCount,
            seriousFindingCount,
            moderateFindingCount,
            minorFindingCount,
            wcagLevelACount,
            wcagLevelAaCount,
            otherWcagCount);


        if (remediationItems.Count > 0)
        {
            AddSectionHeading(
                section,
                "Remediation Snapshot");

            AddRemediationSummary(
                section,
                remediationItems.Count,
                remediationOpen,
                remediationInProgress,
                remediationAwaitingVerification,
                remediationVerified,
                remediationWontFix);
        }

        // =====================================================
        // PAGE 2 - Workflow states
        // =====================================================

        section.AddPageBreak();


        AddSectionHeading(
            section,
            "Scanned Workflow States");

        AddSectionIntroduction(
            section,
            "Summary of each rendered application state included " +
            "in this authenticated accessibility audit.");

        AddWorkflowStateTable(
            section,
            audit);


        // =====================================================
        // Findings
        // =====================================================

        section.AddPageBreak();


        AddSectionHeading(
            section,
            "Accessibility Findings");

        AddSectionIntroduction(
            section,
            "Findings are ordered by remediation priority. " +
            "Critical and serious WCAG Level A or AA violations " +
            "are identified as Fix First.");


        if (allFindings.Count == 0)
        {
            AddEmptyMessage(
                section,
                "No violations or needs-review findings were saved " +
                "for this audit.");
        }
        else
        {
            foreach (
                AuthenticatedAuditFindingDetailsViewModel finding
                in allFindings
                    .OrderBy(finding =>
                        finding.PriorityRank)
                    .ThenBy(finding =>
                        finding.RuleId))
            {
                AuthenticatedAuditStepDetailsViewModel? sourceStep =
                    audit.Steps.FirstOrDefault(step =>
                        step.Findings.Contains(
                            finding));

                AddFinding(
                    section,
                    finding,
                    sourceStep);
            }
        }


        // =====================================================
        // Technical appendix
        // =====================================================

        List<AuthenticatedAuditFindingDetailsViewModel>
            findingsWithNodes =
                allFindings
                    .Where(finding =>
                        finding.Nodes.Count > 0)
                    .OrderBy(finding =>
                        finding.PriorityRank)
                    .ThenBy(finding =>
                        finding.RuleId)
                    .ToList();


        if (findingsWithNodes.Count > 0)
        {
            section.AddPageBreak();


            AddSectionHeading(
                section,
                "Technical Appendix");

            AddSectionIntroduction(
                section,
                "This appendix is intended for technical remediation " +
                "work. It contains saved CSS selectors, axe-core " +
                "failure summaries, element-level guidance, and " +
                "encoded HTML snippets.");


            foreach (
                AuthenticatedAuditFindingDetailsViewModel finding
                in findingsWithNodes)
            {
                AuthenticatedAuditStepDetailsViewModel? sourceStep =
                    audit.Steps.FirstOrDefault(step =>
                        step.Findings.Contains(
                            finding));

                AddTechnicalFinding(
                    section,
                    finding,
                    sourceStep);
            }
        }


        PdfDocumentRenderer renderer =
            new()
            {
                Document = document
            };

        renderer.RenderDocument();


        using MemoryStream stream =
            new();

        renderer.PdfDocument.Save(
            stream,
            closeStream: false);

        return stream.ToArray();
    }


    // =========================================================
    // Document configuration
    // =========================================================

    private static void ConfigureStyles(
        Document document)
    {
        Style normalStyle =
            document.Styles[
                StyleNames.Normal]!;

        normalStyle.Font.Name =
            "Arial";

        normalStyle.Font.Size =
            Unit.FromPoint(9.5);

        normalStyle.Font.Color =
            DarkText;

        normalStyle.ParagraphFormat.SpaceAfter =
            Unit.FromPoint(3);


        Style heading1Style =
            document.Styles[
                StyleNames.Heading1]!;

        heading1Style.Font.Name =
            "Arial";

        heading1Style.Font.Size =
            Unit.FromPoint(15);

        heading1Style.Font.Bold =
            true;

        heading1Style.Font.Color =
            Navy;

        heading1Style.ParagraphFormat.SpaceBefore =
            Unit.FromPoint(12);

        heading1Style.ParagraphFormat.SpaceAfter =
            Unit.FromPoint(7);


        Style heading2Style =
            document.Styles[
                StyleNames.Heading2]!;

        heading2Style.Font.Name =
            "Arial";

        heading2Style.Font.Size =
            Unit.FromPoint(11.5);

        heading2Style.Font.Bold =
            true;

        heading2Style.Font.Color =
            Navy;

        heading2Style.ParagraphFormat.SpaceBefore =
            Unit.FromPoint(8);

        heading2Style.ParagraphFormat.SpaceAfter =
            Unit.FromPoint(5);
    }


    private static void ConfigurePage(
        Section section)
    {
        section.PageSetup.PageFormat =
            PageFormat.Letter;

        section.PageSetup.TopMargin =
            Unit.FromInch(0.55);

        section.PageSetup.BottomMargin =
            Unit.FromInch(0.62);

        section.PageSetup.LeftMargin =
            Unit.FromInch(0.7);

        section.PageSetup.RightMargin =
            Unit.FromInch(0.7);

        section.PageSetup.FooterDistance =
            Unit.FromInch(0.3);
    }


    private static void AddFooter(
        Section section)
    {
        Paragraph footer =
            section.Footers.Primary
                .AddParagraph();

        footer.Format.Alignment =
            ParagraphAlignment.Center;

        footer.Format.Font.Name =
            "Arial";

        footer.Format.Font.Size =
            Unit.FromPoint(7.5);

        footer.Format.Font.Color =
            MutedText;

        footer.AddText(
            "City of Los Angeles · Bureau of Engineering · " +
            "City Website Audit Dashboard · Page ");

        footer.AddPageField();

        footer.AddText(
            " of ");

        footer.AddNumPagesField();
    }


    // =========================================================
    // Shared table configuration
    // =========================================================

    private static void ConfigureTable(
        Table table,
        double horizontalPadding = 5,
        double verticalPadding = 5)
    {
        /*
         * MigraDoc padding belongs on the Table, Row, or Column.
         * It cannot be assigned directly to an individual Cell.
         */
        table.LeftPadding =
            Unit.FromPoint(
                horizontalPadding);

        table.RightPadding =
            Unit.FromPoint(
                horizontalPadding);

        table.TopPadding =
            Unit.FromPoint(
                verticalPadding);

        table.BottomPadding =
            Unit.FromPoint(
                verticalPadding);

        table.Rows.LeftIndent =
            Unit.Zero;
    }


    // =========================================================
    // Report header
    // =========================================================

    private static void AddReportBanner(
        Section section,
        AuthenticatedAuditDetailsViewModel audit)
    {
        Table banner =
            section.AddTable();

        banner.AddColumn(
            Unit.FromInch(7.1));

        ConfigureTable(
            banner,
            horizontalPadding: 16,
            verticalPadding: 14);


        Cell cell =
            banner.AddRow()
                .Cells[0];

        cell.Shading.Color =
            Navy;


        Paragraph city =
            cell.AddParagraph();

        city.Format.Font.Name =
            "Arial";

        city.Format.Font.Size =
            Unit.FromPoint(8);

        city.Format.Font.Bold =
            true;

        city.Format.Font.Color =
            Color.FromRgb(
                205,
                222,
                233);

        city.Format.SpaceAfter =
            Unit.FromPoint(4);

        city.AddText(
            "CITY OF LOS ANGELES · BUREAU OF ENGINEERING");


        Paragraph title =
            cell.AddParagraph();

        title.Format.Font.Name =
            "Arial";

        title.Format.Font.Size =
            Unit.FromPoint(20);

        title.Format.Font.Bold =
            true;

        title.Format.Font.Color =
            Color.FromRgb(
                255,
                255,
                255);

        title.Format.SpaceAfter =
            Unit.FromPoint(5);

        title.AddText(
            "Accessibility Audit Report");


        Paragraph application =
            cell.AddParagraph();

        application.Format.Font.Name =
            "Arial";

        application.Format.Font.Size =
            Unit.FromPoint(11);

        application.Format.Font.Color =
            Color.FromRgb(
                230,
                239,
                245);

        application.AddText(
            $"{audit.ApplicationName} · Audit Run #{audit.Id}");


        Paragraph spacer =
            section.AddParagraph();

        spacer.Format.SpaceAfter =
            Unit.FromPoint(3);
    }


    private static void AddReportMetadata(
        Section section,
        AuthenticatedAuditDetailsViewModel audit)
    {
        Table table =
            section.AddTable();

        table.AddColumn(
            Unit.FromInch(1.6));

        table.AddColumn(
            Unit.FromInch(5.5));

        table.Borders.Width =
            Unit.FromPoint(0.5);

        table.Borders.Color =
            BorderColor;

        ConfigureTable(table);


        AddKeyValueRow(
            table,
            "Application",
            audit.ApplicationName);

        AddKeyValueRow(
            table,
            "Audit Run",
            $"#{audit.Id}");

        AddKeyValueRow(
            table,
            "Status",
            audit.Status);

        AddKeyValueRow(
            table,
            "Accessibility Engine",
            audit.AccessibilityEngine);

        AddKeyValueRow(
            table,
            "Starting URL",
            audit.StartingUrl);

        AddKeyValueRow(
            table,
            "Started",
            audit.StartedAt.ToString(
                "MMM d, yyyy h:mm tt 'UTC'"));

        AddKeyValueRow(
            table,
            "Completed",
            audit.CompletedAt?.ToString(
                "MMM d, yyyy h:mm tt 'UTC'")
            ?? "Not completed");

        AddKeyValueRow(
            table,
            "Report Generated",
            DateTime.UtcNow.ToString(
                "MMM d, yyyy h:mm tt 'UTC'"));


        Paragraph spacer =
            section.AddParagraph();

        spacer.Format.SpaceAfter =
            Unit.FromPoint(2);
    }


    private static void AddKeyValueRow(
        Table table,
        string label,
        string? value)
    {
        Row row =
            table.AddRow();

        Cell labelCell =
            row.Cells[0];

        Cell valueCell =
            row.Cells[1];

        labelCell.Shading.Color =
            LightGray;


        Paragraph labelParagraph =
            labelCell.AddParagraph();

        labelParagraph.Format.Font.Bold =
            true;

        labelParagraph.Format.Font.Size =
            Unit.FromPoint(8.5);

        labelParagraph.AddText(
            label);


        Paragraph valueParagraph =
            valueCell.AddParagraph();

        valueParagraph.Format.Font.Size =
            Unit.FromPoint(8.5);

        valueParagraph.AddText(
            DisplayValue(value));
    }


    // =========================================================
    // Headings
    // =========================================================

    private static void AddSectionHeading(
        Section section,
        string text)
    {
        Paragraph heading =
            section.AddParagraph();

        heading.Style =
            StyleNames.Heading1;

        heading.AddText(
            text);
    }


    private static void AddSectionIntroduction(
        Section section,
        string text)
    {
        Paragraph paragraph =
            section.AddParagraph();

        paragraph.Format.Font.Color =
            MutedText;

        paragraph.Format.SpaceAfter =
            Unit.FromPoint(8);

        paragraph.AddText(
            text);
    }


    // =========================================================
    // Executive summary
    // =========================================================

    private static void AddExecutiveSummary(
        Section section,
        AuthenticatedAuditDetailsViewModel audit,
        int violationFindingCount,
        int needsReviewFindingCount,
        int fixFirstFindingCount,
        int criticalFindingCount,
        int seriousFindingCount)
    {
        Table table =
            section.AddTable();

        for (int columnIndex = 0;
             columnIndex < 4;
             columnIndex++)
        {
            table.AddColumn(
                Unit.FromInch(1.775));
        }

        table.Borders.Width =
            Unit.FromPoint(0.5);

        table.Borders.Color =
            BorderColor;

        ConfigureTable(
            table,
            horizontalPadding: 7,
            verticalPadding: 7);


        Row firstRow =
            table.AddRow();

        AddMetricCell(
            firstRow.Cells[0],
            "States Scanned",
            audit.Steps.Count.ToString(),
            Navy);

        AddMetricCell(
            firstRow.Cells[1],
            "Successful",
            audit.SuccessfulStepCount.ToString(),
            Success);

        AddMetricCell(
            firstRow.Cells[2],
            "Violations",
            violationFindingCount.ToString(),
            Danger);

        AddMetricCell(
            firstRow.Cells[3],
            "Affected Elements",
            audit.TotalAffectedElementCount.ToString(),
            Navy);


        Row secondRow =
            table.AddRow();

        AddMetricCell(
            secondRow.Cells[0],
            "Fix First",
            fixFirstFindingCount.ToString(),
            Danger);

        AddMetricCell(
            secondRow.Cells[1],
            "Needs Review",
            needsReviewFindingCount.ToString(),
            Warning);

        AddMetricCell(
            secondRow.Cells[2],
            "Critical",
            criticalFindingCount.ToString(),
            Danger);

        AddMetricCell(
            secondRow.Cells[3],
            "Serious",
            seriousFindingCount.ToString(),
            Warning);
    }


    private static void AddMetricCell(
        Cell cell,
        string label,
        string value,
        Color valueColor)
    {
        cell.VerticalAlignment =
            VerticalAlignment.Center;


        Paragraph labelParagraph =
            cell.AddParagraph();

        labelParagraph.Format.Alignment =
            ParagraphAlignment.Center;

        labelParagraph.Format.Font.Name =
            "Arial";

        labelParagraph.Format.Font.Size =
            Unit.FromPoint(7.5);

        labelParagraph.Format.Font.Bold =
            true;

        labelParagraph.Format.Font.Color =
            MutedText;

        labelParagraph.Format.SpaceAfter =
            Unit.FromPoint(3);

        labelParagraph.AddText(
            label.ToUpperInvariant());


        Paragraph valueParagraph =
            cell.AddParagraph();

        valueParagraph.Format.Alignment =
            ParagraphAlignment.Center;

        valueParagraph.Format.Font.Name =
            "Arial";

        valueParagraph.Format.Font.Size =
            Unit.FromPoint(17);

        valueParagraph.Format.Font.Bold =
            true;

        valueParagraph.Format.Font.Color =
            valueColor;

        valueParagraph.AddText(
            value);
    }


    // =========================================================
    // Severity / WCAG summary
    // =========================================================

    private static void AddSeveritySummary(
        Section section,
        int critical,
        int serious,
        int moderate,
        int minor,
        int wcagA,
        int wcagAa,
        int otherWcag)
    {
        Table table =
            section.AddTable();

        table.AddColumn(
            Unit.FromInch(3.5));

        table.AddColumn(
            Unit.FromInch(3.6));

        table.Borders.Width =
            Unit.FromPoint(0.5);

        table.Borders.Color =
            BorderColor;

        ConfigureTable(
            table,
            horizontalPadding: 7,
            verticalPadding: 6);


        Row header =
            table.AddRow();

        AddTableHeaderCell(
            header.Cells[0],
            "Severity");

        AddTableHeaderCell(
            header.Cells[1],
            "WCAG Mapping");


        Row row =
            table.AddRow();

        AddSummaryListCell(
            row.Cells[0],
            new[]
            {
                ("Critical", critical.ToString(), Danger),
                ("Serious", serious.ToString(), Warning),
                ("Moderate", moderate.ToString(), Navy),
                ("Minor", minor.ToString(), MutedText)
            });

        AddSummaryListCell(
            row.Cells[1],
            new[]
            {
                ("Level A", wcagA.ToString(), Navy),
                ("Level AA", wcagAa.ToString(), Blue),
                ("Other / Best Practice",
                    otherWcag.ToString(),
                    MutedText)
            });
    }


    private static void AddSummaryListCell(
        Cell cell,
        IEnumerable<(string Label, string Value, Color Color)>
            items)
    {
        foreach (
            (string label,
             string value,
             Color color)
            in items)
        {
            Paragraph paragraph =
                cell.AddParagraph();

            paragraph.Format.SpaceAfter =
                Unit.FromPoint(3);

            paragraph.AddText(
                $"{label}: ");

            FormattedText formattedValue =
                paragraph.AddFormattedText(
                    value,
                    TextFormat.Bold);

            formattedValue.Color =
                color;
        }
    }


    // =========================================================
    // Remediation summary
    // =========================================================

    private static void AddRemediationSummary(
        Section section,
        int total,
        int open,
        int inProgress,
        int awaitingVerification,
        int verified,
        int wontFix)
    {
        Table table =
            section.AddTable();

        for (int index = 0;
             index < 3;
             index++)
        {
            table.AddColumn(
                Unit.FromInch(2.366));
        }

        table.Borders.Width =
            Unit.FromPoint(0.5);

        table.Borders.Color =
            BorderColor;

        ConfigureTable(
            table,
            horizontalPadding: 7,
            verticalPadding: 7);


        Row first =
            table.AddRow();

        AddMetricCell(
            first.Cells[0],
            "Tracked",
            total.ToString(),
            Navy);

        AddMetricCell(
            first.Cells[1],
            "Open",
            open.ToString(),
            Danger);

        AddMetricCell(
            first.Cells[2],
            "In Progress",
            inProgress.ToString(),
            Blue);


        Row second =
            table.AddRow();

        AddMetricCell(
            second.Cells[0],
            "Awaiting Verification",
            awaitingVerification.ToString(),
            Warning);

        AddMetricCell(
            second.Cells[1],
            "Verified",
            verified.ToString(),
            Success);

        AddMetricCell(
            second.Cells[2],
            "Won't Fix",
            wontFix.ToString(),
            MutedText);
    }

    // =========================================================
    // Workflow-state summary table
    // =========================================================

    private static void AddWorkflowStateTable(
        Section section,
        AuthenticatedAuditDetailsViewModel audit)
    {
        Table table =
            section.AddTable();

        table.AddColumn(
            Unit.FromInch(0.45));

        table.AddColumn(
            Unit.FromInch(2.75));

        table.AddColumn(
            Unit.FromInch(0.85));

        table.AddColumn(
            Unit.FromInch(0.95));

        table.AddColumn(
            Unit.FromInch(1.0));

        table.AddColumn(
            Unit.FromInch(1.1));

        table.Borders.Width =
            Unit.FromPoint(0.5);

        table.Borders.Color =
            BorderColor;

        ConfigureTable(table);


        Row header =
            table.AddRow();

        header.HeadingFormat =
            true;


        AddTableHeaderCell(
            header.Cells[0],
            "#");

        AddTableHeaderCell(
            header.Cells[1],
            "State");

        AddTableHeaderCell(
            header.Cells[2],
            "Result");

        AddTableHeaderCell(
            header.Cells[3],
            "Violations");

        AddTableHeaderCell(
            header.Cells[4],
            "Elements");

        AddTableHeaderCell(
            header.Cells[5],
            "Needs Review");


        foreach (
            AuthenticatedAuditStepDetailsViewModel step
            in audit.Steps
                .OrderBy(step =>
                    step.StepNumber))
        {
            Row row =
                table.AddRow();


            AddTableBodyCell(
                row.Cells[0],
                step.StepNumber.ToString(),
                center: true);


            string stateName =
                !string.IsNullOrWhiteSpace(
                    step.StepName)
                    ? step.StepName
                    : step.PageTitle
                    ?? "Unnamed state";


            Cell stateCell =
                row.Cells[1];

            ConfigureBodyCell(
                stateCell);


            Paragraph stateParagraph =
                stateCell.AddParagraph();

            stateParagraph.Format.Font.Bold =
                true;

            stateParagraph.AddText(
                stateName);


            if (!string.IsNullOrWhiteSpace(
                step.PageTitle) &&
                !string.Equals(
                    stateName,
                    step.PageTitle,
                    StringComparison.Ordinal))
            {
                Paragraph titleParagraph =
                    stateCell.AddParagraph();

                titleParagraph.Format.Font.Size =
                    Unit.FromPoint(7.5);

                titleParagraph.Format.Font.Color =
                    MutedText;

                titleParagraph.AddText(
                    step.PageTitle);
            }


            string scanResult =
                step.ScanSucceeded
                    ? "Succeeded"
                    : "Failed";


            AddTableBodyCell(
                row.Cells[2],
                scanResult,
                center: true,
                textColor:
                    step.ScanSucceeded
                        ? Success
                        : Danger);


            AddTableBodyCell(
                row.Cells[3],
                step.ViolationRuleCount.ToString(),
                center: true);


            AddTableBodyCell(
                row.Cells[4],
                step.AffectedElementCount.ToString(),
                center: true);


            AddTableBodyCell(
                row.Cells[5],
                step.NeedsReviewRuleCount.ToString(),
                center: true);
        }


        if (audit.Steps.Count == 0)
        {
            Row row =
                table.AddRow();

            row.Cells[0].MergeRight =
                5;

            Paragraph paragraph =
                row.Cells[0]
                    .AddParagraph();

            paragraph.Format.Alignment =
                ParagraphAlignment.Center;

            paragraph.Format.Font.Color =
                MutedText;

            paragraph.AddText(
                "No rendered workflow states were saved.");
        }
    }


    private static void AddTableHeaderCell(
        Cell cell,
        string text)
    {
        cell.Shading.Color =
            Navy;

        cell.VerticalAlignment =
            VerticalAlignment.Center;


        Paragraph paragraph =
            cell.AddParagraph();

        paragraph.Format.Font.Name =
            "Arial";

        paragraph.Format.Font.Size =
            Unit.FromPoint(7.5);

        paragraph.Format.Font.Bold =
            true;

        paragraph.Format.Font.Color =
            Color.FromRgb(
                255,
                255,
                255);

        paragraph.AddText(
            text);
    }


    private static void AddTableBodyCell(
        Cell cell,
        string? text,
        bool center = false,
        Color? textColor = null)
    {
        ConfigureBodyCell(
            cell);


        Paragraph paragraph =
            cell.AddParagraph();

        if (center)
        {
            paragraph.Format.Alignment =
                ParagraphAlignment.Center;
        }

        paragraph.Format.Font.Size =
            Unit.FromPoint(8);


        if (textColor.HasValue)
        {
            paragraph.Format.Font.Color =
                textColor.Value;

            paragraph.Format.Font.Bold =
                true;
        }


        paragraph.AddText(
            DisplayValue(text));
    }


    private static void ConfigureBodyCell(
        Cell cell)
    {
        cell.VerticalAlignment =
            VerticalAlignment.Center;
    }


    // =========================================================
    // Main findings
    // =========================================================

    private static void AddFinding(
        Section section,
        AuthenticatedAuditFindingDetailsViewModel finding,
        AuthenticatedAuditStepDetailsViewModel? sourceStep)
    {
        bool isFixFirst =
            IsFixFirstFinding(
                finding);


        Table headingTable =
            section.AddTable();

        headingTable.AddColumn(
            Unit.FromInch(7.1));

        headingTable.Borders.Width =
            Unit.FromPoint(0.5);

        headingTable.Borders.Color =
            isFixFirst
                ? Danger
                : BorderColor;

        ConfigureTable(
            headingTable,
            horizontalPadding: 8,
            verticalPadding: 7);


        Cell headingCell =
            headingTable.AddRow()
                .Cells[0];

        headingCell.Shading.Color =
            isFixFirst
                ? Color.FromRgb(
                    252,
                    237,
                    238)
                : LightGray;


        Paragraph heading =
            headingCell.AddParagraph();

        heading.Format.Font.Size =
            Unit.FromPoint(11);

        heading.Format.Font.Bold =
            true;

        heading.Format.Font.Color =
            isFixFirst
                ? Danger
                : Navy;

        heading.AddText(
            string.IsNullOrWhiteSpace(
                finding.Help)
                ? DisplayValue(
                    finding.RuleId)
                : finding.Help);


        Paragraph rule =
            headingCell.AddParagraph();

        rule.Format.Font.Size =
            Unit.FromPoint(8);

        rule.Format.Font.Color =
            MutedText;

        rule.AddText(
            $"axe rule: {DisplayValue(finding.RuleId)}");


        if (isFixFirst)
        {
            Paragraph priority =
                headingCell.AddParagraph();

            priority.Format.Font.Size =
                Unit.FromPoint(8);

            priority.Format.Font.Bold =
                true;

            priority.Format.Font.Color =
                Danger;

            priority.AddText(
                "FIX FIRST");
        }


        Table details =
            section.AddTable();

        details.AddColumn(
            Unit.FromInch(1.1));

        details.AddColumn(
            Unit.FromInch(2.45));

        details.AddColumn(
            Unit.FromInch(1.1));

        details.AddColumn(
            Unit.FromInch(2.45));

        details.Borders.Width =
            Unit.FromPoint(0.5);

        details.Borders.Color =
            BorderColor;

        ConfigureTable(
            details,
            horizontalPadding: 6,
            verticalPadding: 5);


        string sourceStepDisplay =
            sourceStep is null
                ? "Not available"
                : $"Step {sourceStep.StepNumber}: " +
                  GetStepDisplayName(
                      sourceStep);


        AddFindingDetailRow(
            details,
            "State",
            sourceStepDisplay,
            "Finding Type",
            GetFindingTypeDisplay(
                finding.FindingType));


        AddFindingDetailRow(
            details,
            "Severity",
            GetImpactDisplay(
                finding.Impact),
            "WCAG",
            finding.WcagLevel
                ?? "Other / Best Practice");


        AddFindingDetailRow(
            details,
            "Elements",
            finding.AffectedElementCount.ToString(),
            "Remediation",
            GetRemediationDisplay(
                finding));


        if (!string.IsNullOrWhiteSpace(
            finding.Description))
        {
            AddFindingTextSection(
                section,
                "Description",
                finding.Description);
        }


        if (!string.IsNullOrWhiteSpace(
            finding.Help))
        {
            AddFindingTextSection(
                section,
                "Recommended Fix",
                finding.Help);
        }


        if (!string.IsNullOrWhiteSpace(
            finding.HelpUrl))
        {
            AddFindingTextSection(
                section,
                "Guidance",
                finding.HelpUrl);
        }


        if (!string.IsNullOrWhiteSpace(
            finding.WcagTags))
        {
            AddFindingTextSection(
                section,
                "WCAG Tags",
                finding.WcagTags,
                smallerText: true);
        }


        Paragraph spacer =
            section.AddParagraph();

        spacer.Format.SpaceAfter =
            Unit.FromPoint(6);
    }


    private static void AddFindingDetailRow(
        Table table,
        string label1,
        string value1,
        string label2,
        string value2)
    {
        Row row =
            table.AddRow();


        AddFindingLabelCell(
            row.Cells[0],
            label1);

        AddFindingValueCell(
            row.Cells[1],
            value1);

        AddFindingLabelCell(
            row.Cells[2],
            label2);

        AddFindingValueCell(
            row.Cells[3],
            value2);
    }


    private static void AddFindingLabelCell(
        Cell cell,
        string label)
    {
        cell.VerticalAlignment =
            VerticalAlignment.Center;

        cell.Shading.Color =
            LightGray;


        Paragraph paragraph =
            cell.AddParagraph();

        paragraph.Format.Font.Size =
            Unit.FromPoint(7.5);

        paragraph.Format.Font.Bold =
            true;

        paragraph.Format.Font.Color =
            MutedText;

        paragraph.AddText(
            label);
    }


    private static void AddFindingValueCell(
        Cell cell,
        string? value)
    {
        cell.VerticalAlignment =
            VerticalAlignment.Center;


        Paragraph paragraph =
            cell.AddParagraph();

        paragraph.Format.Font.Size =
            Unit.FromPoint(8);

        paragraph.AddText(
            DisplayValue(value));
    }


    private static void AddFindingTextSection(
        Section section,
        string label,
        string? value,
        bool smallerText = false)
    {
        if (string.IsNullOrWhiteSpace(
            value))
        {
            return;
        }


        Paragraph paragraph =
            section.AddParagraph();

        paragraph.Format.LeftIndent =
            Unit.FromPoint(7);

        paragraph.Format.RightIndent =
            Unit.FromPoint(7);

        paragraph.Format.SpaceBefore =
            Unit.FromPoint(4);

        paragraph.Format.SpaceAfter =
            Unit.FromPoint(2);

        paragraph.Format.Font.Size =
            Unit.FromPoint(
                smallerText
                    ? 7.5
                    : 8.5);


        FormattedText labelText =
            paragraph.AddFormattedText(
                $"{label}: ",
                TextFormat.Bold);

        labelText.Color =
            Navy;


        paragraph.AddText(
            value);
    }


    // =========================================================
    // Technical appendix
    // =========================================================

    private static void AddTechnicalFinding(
        Section section,
        AuthenticatedAuditFindingDetailsViewModel finding,
        AuthenticatedAuditStepDetailsViewModel? sourceStep)
    {
        Paragraph heading =
            section.AddParagraph();

        heading.Style =
            StyleNames.Heading2;

        heading.AddText(
            DisplayValue(
                finding.RuleId));


        Paragraph context =
            section.AddParagraph();

        context.Format.Font.Size =
            Unit.FromPoint(8);

        context.Format.Font.Color =
            MutedText;

        context.Format.SpaceAfter =
            Unit.FromPoint(5);


        context.AddText(
            sourceStep is null
                ? "Source state unavailable"
                : $"Step {sourceStep.StepNumber}: " +
                  $"{GetStepDisplayName(sourceStep)} · " +
                  $"{sourceStep.Url}");


        for (int nodeIndex = 0;
             nodeIndex < finding.Nodes.Count;
             nodeIndex++)
        {
            AuthenticatedAuditFindingNodeDetailsViewModel node =
                finding.Nodes[nodeIndex];

            AddTechnicalNode(
                section,
                node,
                nodeIndex + 1);
        }


        Paragraph spacer =
            section.AddParagraph();

        spacer.Format.SpaceAfter =
            Unit.FromPoint(4);
    }


    private static void AddTechnicalNode(
        Section section,
        AuthenticatedAuditFindingNodeDetailsViewModel node,
        int number)
    {
        Table table =
            section.AddTable();

        table.AddColumn(
            Unit.FromInch(1.35));

        table.AddColumn(
            Unit.FromInch(5.75));

        table.Borders.Width =
            Unit.FromPoint(0.5);

        table.Borders.Color =
            BorderColor;

        ConfigureTable(
            table,
            horizontalPadding: 6,
            verticalPadding: 5);


        Row headingRow =
            table.AddRow();

        headingRow.Cells[0].MergeRight =
            1;

        headingRow.Cells[0].Shading.Color =
            LightGray;


        Paragraph heading =
            headingRow.Cells[0]
                .AddParagraph();

        heading.Format.Font.Bold =
            true;

        heading.Format.Font.Size =
            Unit.FromPoint(8.5);

        heading.Format.Font.Color =
            Navy;

        heading.AddText(
            $"Affected Element {number}");


        AddTechnicalRow(
            table,
            "CSS Selector / Target",
            node.Target);

        AddTechnicalRow(
            table,
            "Failure Summary",
            node.FailureSummary);

        AddTechnicalRow(
            table,
            "Element Fix Guidance",
            node.ElementFixGuidance);

        AddTechnicalRow(
            table,
            "HTML Snippet",
            node.Html,
            smallerText: true);


        Paragraph spacer =
            section.AddParagraph();

        spacer.Format.SpaceAfter =
            Unit.FromPoint(4);
    }


    private static void AddTechnicalRow(
        Table table,
        string label,
        string? value,
        bool smallerText = false)
    {
        Row row =
            table.AddRow();


        Cell labelCell =
            row.Cells[0];

        Cell valueCell =
            row.Cells[1];


        labelCell.Shading.Color =
            LightGray;


        Paragraph labelParagraph =
            labelCell.AddParagraph();

        labelParagraph.Format.Font.Size =
            Unit.FromPoint(7.5);

        labelParagraph.Format.Font.Bold =
            true;

        labelParagraph.Format.Font.Color =
            MutedText;

        labelParagraph.AddText(
            label);


        Paragraph valueParagraph =
            valueCell.AddParagraph();

        valueParagraph.Format.Font.Size =
            Unit.FromPoint(
                smallerText
                    ? 7
                    : 8);

        valueParagraph.AddText(
            DisplayValue(value));
    }


    // =========================================================
    // Empty-state box
    // =========================================================

    private static void AddEmptyMessage(
        Section section,
        string message)
    {
        Table table =
            section.AddTable();

        table.AddColumn(
            Unit.FromInch(7.1));

        table.Borders.Width =
            Unit.FromPoint(0.5);

        table.Borders.Color =
            BorderColor;

        ConfigureTable(
            table,
            horizontalPadding: 10,
            verticalPadding: 10);


        Cell cell =
            table.AddRow()
                .Cells[0];

        cell.Shading.Color =
            LightGray;


        Paragraph paragraph =
            cell.AddParagraph();

        paragraph.Format.Alignment =
            ParagraphAlignment.Center;

        paragraph.Format.Font.Color =
            MutedText;

        paragraph.AddText(
            message);
    }


    // =========================================================
    // Display helpers
    // =========================================================

    private static bool IsFixFirstFinding(
        AuthenticatedAuditFindingDetailsViewModel finding)
    {
        bool highImpact =
            string.Equals(
                finding.Impact,
                "critical",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                finding.Impact,
                "serious",
                StringComparison.OrdinalIgnoreCase);


        bool wcagPriority =
            string.Equals(
                finding.WcagLevel,
                "A",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                finding.WcagLevel,
                "AA",
                StringComparison.OrdinalIgnoreCase);


        return
            highImpact &&
            wcagPriority;
    }


    private static string GetStepDisplayName(
        AuthenticatedAuditStepDetailsViewModel step)
    {
        if (!string.IsNullOrWhiteSpace(
            step.StepName))
        {
            return
                step.StepName;
        }


        if (!string.IsNullOrWhiteSpace(
            step.PageTitle))
        {
            return
                step.PageTitle;
        }


        return
            "Unnamed state";
    }


    private static string GetFindingTypeDisplay(
        string? findingType)
    {
        if (string.Equals(
            findingType,
            "NeedsReview",
            StringComparison.OrdinalIgnoreCase))
        {
            return
                "Needs Manual Review";
        }


        if (string.Equals(
            findingType,
            "Violation",
            StringComparison.OrdinalIgnoreCase))
        {
            return
                "Violation";
        }


        return
            DisplayValue(
                findingType);
    }


    private static string GetImpactDisplay(
        string? impact)
    {
        if (string.IsNullOrWhiteSpace(
            impact))
        {
            return
                "Unknown";
        }


        return impact.ToLowerInvariant()
            switch
        {
            "critical" =>
                "Critical",

            "serious" =>
                "Serious",

            "moderate" =>
                "Moderate",

            "minor" =>
                "Minor",

            _ =>
                impact
        };
    }


    private static string GetRemediationDisplay(
        AuthenticatedAuditFindingDetailsViewModel finding)
    {
        if (!finding.RemediationItemId.HasValue)
        {
            return
                "Not tracked";
        }


        string status =
            GetRemediationStatusDisplay(
                finding.RemediationStatus);


        return
            $"#{finding.RemediationItemId.Value} · {status}";
    }


    private static string GetRemediationStatusDisplay(
        string? status)
    {
        return NormalizeRemediationStatus(
            status)
            switch
        {
            "open" =>
                "Open",

            "inprogress" =>
                "In Progress",

            "fixed" =>
                "Fixed - Awaiting Verification",

            "verified" =>
                "Verified",

            "wontfix" =>
                "Won't Fix",

            _ =>
                DisplayValue(
                    status)
        };
    }


    private static string NormalizeRemediationStatus(
        string? status)
    {
        if (string.IsNullOrWhiteSpace(
            status))
        {
            return
                string.Empty;
        }


        string normalized =
            status
                .Trim()
                .ToLowerInvariant()
                .Replace(
                    " ",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "-",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "–",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "'",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "’",
                    string.Empty,
                    StringComparison.Ordinal);


        if (normalized is
            "fixedawaitingverification" or
            "awaitingverification")
        {
            return
                "fixed";
        }


        return
            normalized;
    }


    private static string DisplayValue(
        string? value)
    {
        return
            string.IsNullOrWhiteSpace(
                value)
                ? "Not available"
                : value;
    }
}
