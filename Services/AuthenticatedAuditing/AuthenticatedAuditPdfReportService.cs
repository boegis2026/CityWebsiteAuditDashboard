using CityWebsiteAuditDashboard.ViewModels;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;

namespace CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;

public sealed class AuthenticatedAuditPdfReportService
    : IAuthenticatedAuditPdfReportService
{
    public byte[] CreatePdf(
        AuthenticatedAuditDetailsViewModel audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        var document = new Document();

        document.Info.Title =
            $"{audit.ApplicationName} Accessibility Audit";

        document.Info.Subject =
            "Authenticated accessibility audit report";

        document.Info.Author =
            "City Website Audit Dashboard";

        ConfigureStyles(document);

        var allFindings =
            audit.Steps
                .SelectMany(step => step.Findings)
                .ToList();

        // Critical or Serious WCAG A/AA findings should receive attention first.
        int fixFirstFindingCount = allFindings.Count(finding =>
            string.Equals(
                finding.FindingType,
                "Violation",
                StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(
                finding.Impact,
                "critical",
                StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                finding.Impact,
                "serious",
                StringComparison.OrdinalIgnoreCase)) &&
            (string.Equals(
                finding.WcagLevel,
                "A",
                StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                finding.WcagLevel,
                "AA",
                StringComparison.OrdinalIgnoreCase)));

        Section section = document.AddSection();

        section.PageSetup.PageFormat =
            PageFormat.Letter;

        section.PageSetup.TopMargin =
            Unit.FromInch(0.6);

        section.PageSetup.BottomMargin =
            Unit.FromInch(0.6);

        section.PageSetup.LeftMargin =
            Unit.FromInch(0.7);

        section.PageSetup.RightMargin =
            Unit.FromInch(0.7);

        // Display consistent page numbering throughout the report.
        Paragraph footer =
            section.Footers.Primary.AddParagraph();

        footer.Format.Alignment =
            ParagraphAlignment.Center;

        footer.Format.Font.Size =
            Unit.FromPoint(8);

        footer.AddText(
            "City Website Audit Dashboard — Page ");

        footer.AddPageField();

        footer.AddText(" of ");

        footer.AddNumPagesField();

        Paragraph title = section.AddParagraph();

        title.Format.Font.Size =
            Unit.FromPoint(18);

        title.Format.Font.Bold = true;

        title.Format.SpaceAfter =
            Unit.FromPoint(12);

        title.AddText(
            "Authenticated Accessibility Audit");

        AddLabelValue(
            section,
            "Application",
            audit.ApplicationName);

        AddLabelValue(
            section,
            "Starting URL",
            audit.StartingUrl);

        AddLabelValue(
            section,
            "Accessibility engine",
            audit.AccessibilityEngine);

        AddLabelValue(
            section,
            "Status",
            audit.Status);

        AddLabelValue(
            section,
            "Started",
            audit.StartedAt.ToString(
                "yyyy-MM-dd HH:mm 'UTC'"));

        AddLabelValue(
            section,
            "Completed",
            audit.CompletedAt?.ToString(
                "yyyy-MM-dd HH:mm 'UTC'")
            ?? "Not completed");

        AddLabelValue(
            section,
            "Report generated",
            DateTime.UtcNow.ToString(
                "yyyy-MM-dd HH:mm 'UTC'"));

        section.AddParagraph();

        Paragraph summaryHeading =
            section.AddParagraph();

        summaryHeading.Format.Font.Size =
            Unit.FromPoint(14);

        summaryHeading.Format.Font.Bold = true;

        summaryHeading.Format.SpaceAfter =
            Unit.FromPoint(8);

        summaryHeading.AddText(
            "Overall Summary");

        AddLabelValue(
            section,
            "Total scanned pages",
            audit.Steps.Count.ToString());

        AddLabelValue(
            section,
            "Successful pages",
            audit.SuccessfulStepCount.ToString());

        AddLabelValue(
            section,
            "Failed pages",
            audit.FailedStepCount.ToString());

        AddLabelValue(
            section,
            "Violation rules",
            audit.TotalViolationRuleCount.ToString());

        AddLabelValue(
            section,
            "Needs-review rules",
            allFindings.Count(finding =>
                string.Equals(
                finding.FindingType,
                "NeedsReview",
                StringComparison.OrdinalIgnoreCase))
            .ToString());

        AddLabelValue(
            section,
            "Affected elements",
            audit.TotalAffectedElementCount.ToString());

        AddLabelValue(
            section,
            "Fix first findings",
            fixFirstFindingCount.ToString());

        section.AddParagraph();

        Paragraph severityHeading =
            section.AddParagraph();

        severityHeading.Format.Font.Size =
            Unit.FromPoint(12);

        severityHeading.Format.Font.Bold = true;

        severityHeading.Format.SpaceAfter =
            Unit.FromPoint(6);

        severityHeading.AddText(
            "Findings by Severity");

        AddLabelValue(
            section,
            "Critical",
            allFindings.Count(finding =>
                string.Equals(
                    finding.Impact,
                    "critical",
                    StringComparison.OrdinalIgnoreCase))
                .ToString());

        AddLabelValue(
            section,
            "Serious",
            allFindings.Count(finding =>
                string.Equals(
                    finding.Impact,
                    "serious",
                    StringComparison.OrdinalIgnoreCase))
                .ToString());

        AddLabelValue(
            section,
            "Moderate",
            allFindings.Count(finding =>
                string.Equals(
                    finding.Impact,
                    "moderate",
                    StringComparison.OrdinalIgnoreCase))
                .ToString());

        AddLabelValue(
            section,
            "Minor",
            allFindings.Count(finding =>
                string.Equals(
                    finding.Impact,
                    "minor",
                    StringComparison.OrdinalIgnoreCase))
                .ToString());

        section.AddParagraph();

        Paragraph wcagHeading =
            section.AddParagraph();

        wcagHeading.Format.Font.Size =
            Unit.FromPoint(12);

        wcagHeading.Format.Font.Bold = true;

        wcagHeading.Format.SpaceAfter =
            Unit.FromPoint(6);

        wcagHeading.AddText(
            "WCAG Levels");

        AddLabelValue(
            section,
            "Level A findings",
            allFindings.Count(finding =>
                string.Equals(
                    finding.WcagLevel,
                    "A",
                    StringComparison.OrdinalIgnoreCase))
                .ToString());

        AddLabelValue(
            section,
            "Level AA findings",
            allFindings.Count(finding =>
                string.Equals(
                    finding.WcagLevel,
                    "AA",
                    StringComparison.OrdinalIgnoreCase))
                .ToString());

        AddLabelValue(
            section,
            "Other or best-practice findings",
            allFindings.Count(finding =>
                string.IsNullOrWhiteSpace(
                    finding.WcagLevel))
                .ToString());

        section.AddParagraph();

        Paragraph priorityHeading =
            section.AddParagraph();

        priorityHeading.Format.Font.Size =
            Unit.FromPoint(12);

        priorityHeading.Format.Font.Bold = true;

        priorityHeading.Format.SpaceAfter =
            Unit.FromPoint(6);

        priorityHeading.AddText(
            "Recommended Fix Order");

        Paragraph priorityExplanation =
            section.AddParagraph();

        priorityExplanation.Format.SpaceAfter =
            Unit.FromPoint(6);

        priorityExplanation.AddText(
            "Address Critical findings first, followed by Serious, " +
            "Moderate, and Minor findings. Within the same severity, " +
            "WCAG Level A findings appear before Level AA findings, " +
            "followed by other or best-practice checks.");

        section.AddParagraph();

        Paragraph notesHeading =
            section.AddParagraph();

        notesHeading.Format.Font.Size =
            Unit.FromPoint(12);

        notesHeading.Format.Font.Bold = true;

        notesHeading.Format.SpaceAfter =
            Unit.FromPoint(6);

        notesHeading.AddText(
            "Important Report Notes");

        Paragraph reportNotes =
            section.AddParagraph();

        reportNotes.Format.SpaceAfter =
            Unit.FromPoint(6);

        reportNotes.AddText(
            "This report contains automated axe-core results. " +
            "Automated testing cannot identify every accessibility issue " +
            "and does not by itself confirm full WCAG compliance. " +
            "Needs-review findings require manual evaluation, and the " +
            "application should also be tested with keyboard navigation, " +
            "screen readers, zoom, and other assistive technologies.");

        section.AddParagraph();

        Paragraph pagesHeading =
            section.AddParagraph();

        pagesHeading.Format.Font.Size =
            Unit.FromPoint(14);

        pagesHeading.Format.Font.Bold = true;

        pagesHeading.Format.SpaceBefore =
            Unit.FromPoint(12);

        pagesHeading.Format.SpaceAfter =
            Unit.FromPoint(8);

        pagesHeading.AddText(
            "Scanned Pages");

        foreach (var step in audit.Steps
            .OrderBy(step => step.StepNumber))
        {
            Paragraph stepHeading =
                section.AddParagraph();

            stepHeading.Format.Font.Size =
                Unit.FromPoint(12);

            stepHeading.Format.Font.Bold = true;

            stepHeading.Format.SpaceBefore =
                Unit.FromPoint(10);

            stepHeading.Format.SpaceAfter =
                Unit.FromPoint(4);

            stepHeading.AddText(
                $"Step {step.StepNumber}");

            AddLabelValue(
                section,
                "Scanned URL",
                step.Url);

            AddLabelValue(
                section,
                "Page title",
                step.PageTitle);

            AddLabelValue(
                section,
                "Main heading",
                step.Heading);

            AddLabelValue(
                section,
                "Scan date",
                step.ScannedAt.ToString(
                    "yyyy-MM-dd HH:mm:ss 'UTC'"));

            AddLabelValue(
                section,
                "Scan result",
                step.ScanSucceeded
                    ? "Succeeded"
                    : "Failed");

            if (!string.IsNullOrWhiteSpace(
                step.ErrorMessage))
            {
                AddLabelValue(
                    section,
                    "Scan error",
                    step.ErrorMessage);
            }
        }

        section.AddPageBreak();

        Paragraph findingsHeading =
            section.AddParagraph();

        findingsHeading.Format.Font.Size =
            Unit.FromPoint(14);

        findingsHeading.Format.Font.Bold = true;

        findingsHeading.Format.SpaceAfter =
            Unit.FromPoint(10);

        findingsHeading.AddText(
            "Individual Accessibility Findings");

        if (allFindings.Count == 0)
        {
            section.AddParagraph(
                "No violations or needs-review findings were saved.");
        }
        else
        {
            foreach (AuthenticatedAuditFindingDetailsViewModel finding
                     in allFindings
                         .OrderBy(finding =>
                             finding.PriorityRank)
                         .ThenBy(finding =>
                             finding.RuleId))
            {
                // Find the saved page that produced this accessibility finding.
                AuthenticatedAuditStepDetailsViewModel? sourceStep =
                    audit.Steps.FirstOrDefault(step =>
                        step.Findings.Contains(finding));

                Paragraph findingHeading =
                    section.AddParagraph();

                findingHeading.Format.Font.Size =
                    Unit.FromPoint(12);

                findingHeading.Format.Font.Bold = true;

                findingHeading.Format.SpaceBefore =
                    Unit.FromPoint(10);

                findingHeading.Format.SpaceAfter =
                    Unit.FromPoint(6);

                findingHeading.AddText(
                    string.IsNullOrWhiteSpace(finding.RuleId)
                        ? "Unknown axe rule"
                        : finding.RuleId);

                AddLabelValue(
                    section,
                    "Scanned step",
                    sourceStep?.StepNumber.ToString());

                AddLabelValue(
                    section,
                    "Scanned URL",
                    sourceStep?.Url);

                bool isFixFirst =
                    string.Equals(
                        finding.FindingType,
                        "Violation",
                        StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(
                        finding.Impact,
                        "critical",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        finding.Impact,
                        "serious",
                        StringComparison.OrdinalIgnoreCase)) &&
                    (string.Equals(
                        finding.WcagLevel,
                        "A",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        finding.WcagLevel,
                        "AA",
                        StringComparison.OrdinalIgnoreCase));

                AddLabelValue(
                    section,
                    "Priority",
                    isFixFirst
                        ? "FIX FIRST"
                        : "Standard priority");

                AddLabelValue(
                    section,
                    "Finding type",
                    finding.FindingType);

                AddLabelValue(
                    section,
                    "Impact",
                    finding.Impact);

                AddLabelValue(
                    section,
                    "WCAG level",
                    finding.WcagLevel
                    ?? "Other / Best Practice");

                AddLabelValue(
                    section,
                    "WCAG tags",
                    finding.WcagTags);

                AddLabelValue(
                    section,
                    "Affected elements",
                    finding.AffectedElementCount.ToString());

                AddLabelValue(
                    section,
                    "Recommended fix",
                    finding.Help);

                AddLabelValue(
                    section,
                    "Description",
                    finding.Description);

                AddLabelValue(
                    section,
                    "Guidance URL",
                    finding.HelpUrl);

                if (finding.Nodes.Count > 0)
                {
                    Paragraph elementsHeading =
                        section.AddParagraph();

                    elementsHeading.Format.Font.Bold = true;

                    elementsHeading.Format.SpaceBefore =
                        Unit.FromPoint(6);

                    elementsHeading.Format.SpaceAfter =
                        Unit.FromPoint(4);

                    elementsHeading.AddText(
                        "Exact Affected Elements");

                    for (int nodeIndex = 0;
                         nodeIndex < finding.Nodes.Count;
                         nodeIndex++)
                    {
                        AuthenticatedAuditFindingNodeDetailsViewModel node =
                            finding.Nodes[nodeIndex];

                        Paragraph nodeHeading =
                            section.AddParagraph();

                        nodeHeading.Format.Font.Bold = true;

                        nodeHeading.Format.SpaceBefore =
                            Unit.FromPoint(6);

                        nodeHeading.AddText(
                            $"Affected Element {nodeIndex + 1}");

                        AddLabelValue(
                            section,
                            "CSS selector / target",
                            node.Target);

                        AddLabelValue(
                            section,
                            "Failure summary",
                            node.FailureSummary);

                        /*
                         * MigraDoc adds the saved HTML as encoded text.
                         * It is displayed in the report and is not executed.
                         */
                        AddLabelValue(
                            section,
                            "HTML snippet",
                            node.Html);
                    }
                }
            }
        }

        var renderer =
            new PdfDocumentRenderer
            {
                Document = document
            };

        renderer.RenderDocument();

        using var stream =
            new MemoryStream();

        renderer.PdfDocument.Save(
            stream,
            closeStream: false);

        return stream.ToArray();
    }

    private static void ConfigureStyles(
        Document document)
    {
        // Use a standard Windows font available on the work computer.
        Style normalStyle =
            document.Styles[StyleNames.Normal]!;

        normalStyle.Font.Name = "Arial";

        normalStyle.Font.Size =
            Unit.FromPoint(10);
    }

    private static void AddLabelValue(
        Section section,
        string label,
        string? value)
    {
        Paragraph paragraph =
            section.AddParagraph();

        paragraph.Format.SpaceAfter =
            Unit.FromPoint(4);

        paragraph.AddFormattedText(
            $"{label}: ",
            TextFormat.Bold);

        paragraph.AddText(
            string.IsNullOrWhiteSpace(value)
                ? "Not available"
                : value);
    }
}
