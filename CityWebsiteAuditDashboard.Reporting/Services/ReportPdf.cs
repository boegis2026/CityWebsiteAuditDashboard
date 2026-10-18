using CityWebsiteAuditDashboard.Reporting.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace CityWebsiteAuditDashboard.Reporting.Services;

public sealed class ReportFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(
        string family,
        bool bold,
        bool italic)
    {
        return new FontResolverInfo(
            bold ? "ReportBold" : "ReportRegular");
    }

    public byte[] GetFont(string face)
    {
        var bold = face == "ReportBold";

        var path = OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Fonts),
                bold ? "arialbd.ttf" : "arial.ttf")
            : "/usr/share/fonts/truetype/dejavu/" +
              (bold
                  ? "DejaVuSans-Bold.ttf"
                  : "DejaVuSans.ttf");

        return File.ReadAllBytes(path);
    }
}

public static class ReportPdf
{
    public static byte[] Create(ReportPage model, bool audit)
    {
        var document = new Document();

        document.Info.Title = audit
            ? $"{model.Audit!.Application} - Audit {model.Audit.Id}"
            : "BOE Accessibility Overview";

        document.Info.Author =
            "City of Los Angeles - Bureau of Engineering";

        var normal = document.Styles[StyleNames.Normal]!;

        normal.Font.Name = "Report";
        normal.Font.Size = 10;
        normal.Font.Color = Color.FromRgb(30, 48, 64);
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);

        foreach (var styleName in new[]
        {
            StyleNames.Heading1,
            StyleNames.Heading2,
            StyleNames.Heading3
        })
        {
            var style = document.Styles[styleName]!;

            style.Font.Name = "Report";
            style.Font.Bold = true;
            style.Font.Color = Color.FromRgb(18, 51, 74);

            style.Font.Size = styleName == StyleNames.Heading1
                ? 24
                : styleName == StyleNames.Heading2
                    ? 16
                    : 12;

            style.ParagraphFormat.KeepWithNext = true;
            style.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
        }

        var section = document.AddSection();

        section.PageSetup.PageFormat = PageFormat.Letter;

        section.PageSetup.TopMargin =
            section.PageSetup.BottomMargin = Unit.FromInch(.65);

        section.PageSetup.LeftMargin =
            section.PageSetup.RightMargin = Unit.FromInch(.7);

        section.PageSetup.HeaderDistance = Unit.FromInch(.25);
        section.PageSetup.FooterDistance = Unit.FromInch(.3);

        var header = section.Headers.Primary.AddParagraph(
            "CITY OF LOS ANGELES  /  BUREAU OF ENGINEERING");

        header.Format.Font.Size = 8;
        header.Format.Font.Bold = true;
        header.Format.Font.Color = Color.FromRgb(53, 80, 102);

        var footer = section.Footers.Primary.AddParagraph(
            "BOE Accessibility Reports  •  Page ");

        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
        footer.Format.Font.Size = 8;

        void Text(string? value)
        {
            section.AddParagraph(Wrap(value ?? "Not recorded"));
        }

        void Heading(
            string text,
            string style = StyleNames.Heading2)
        {
            section.AddParagraph(text, style);
        }

        void Panel(
            string label,
            string? text,
            Color background,
            bool code = false)
        {
            var title = section.AddParagraph(label);

            title.Format.Font.Bold = true;
            title.Format.SpaceBefore = Unit.FromPoint(10);
            title.Format.KeepWithNext = true;

            var body = section.AddParagraph(
                Wrap(string.IsNullOrWhiteSpace(text)
                    ? "Not recorded"
                    : text));

            body.Format.Shading.Color = background;

            body.Format.LeftIndent =
                body.Format.RightIndent = Unit.FromPoint(8);

            body.Format.SpaceBefore = Unit.FromPoint(4);
            body.Format.SpaceAfter = Unit.FromPoint(8);

            // Long technical details may continue onto the next page.
            body.Format.KeepTogether = false;

            if (code)
            {
                body.Format.Font.Size = 9;
            }
        }

        void Table(
            string[] headings,
            IEnumerable<string[]> rows,
            double[] widths)
        {
            var table = section.AddTable();

            table.Borders.Color = Color.FromRgb(202, 214, 224);
            table.Borders.Width = .5;

            table.TopPadding =
                table.BottomPadding = Unit.FromPoint(7);

            for (var i = 0; i < headings.Length; i++)
            {
                table.AddColumn(Unit.FromInch(widths[i]));
            }

            var headerRow = table.AddRow();

            // Repeat the column headings when a table spans pages.
            headerRow.HeadingFormat = true;
            headerRow.Shading.Color = Color.FromRgb(18, 51, 74);
            headerRow.Format.Font.Color = Colors.White;
            headerRow.Format.Font.Bold = true;

            for (var i = 0; i < headings.Length; i++)
            {
                headerRow.Cells[i].AddParagraph(headings[i]);
            }

            var index = 0;

            foreach (var values in rows)
            {
                var row = table.AddRow();

                row.Shading.Color = index++ % 2 == 0
                    ? Color.FromRgb(240, 245, 249)
                    : Colors.White;

                for (var i = 0; i < values.Length; i++)
                {
                    row.Cells[i].AddParagraph(Wrap(values[i]));
                }
            }

            section.AddParagraph().Format.SpaceAfter =
                Unit.FromPoint(2);
        }

        void Metrics(params (string Label, long Count)[] metrics)
        {
            Table(
                metrics.Select(x => x.Label).ToArray(),
                new[]
                {
                    metrics
                        .Select(x => ReportDisplay.Number(x.Count))
                        .ToArray()
                },
                metrics.Select(_ => 7.1 / metrics.Length).ToArray());
        }

        void Chart(string title, IEnumerable<CountRow> rows)
        {
            Heading(title);

            var list = rows.ToList();

            var maximum = Math.Max(
                1,
                list.Select(x => x.Count).DefaultIfEmpty().Max());

            foreach (var row in list)
            {
                var label = section.AddParagraph(
                    $"{row.Label}: {ReportDisplay.Number(row.Count)}");

                label.Format.KeepWithNext = true;

                if (row.Count == 0)
                {
                    section.AddParagraph("No recorded items")
                        .Format.Font.Size = 8;

                    continue;
                }

                var bar = section.AddParagraph(" ");

                bar.Format.Font.Size = 5;

                bar.Format.RightIndent = Unit.FromInch(
                    7.1 *
                    (1 - Math.Max(.008, (double)row.Count / maximum)));

                bar.Format.Shading.Color =
                    ReportDisplay.Tone(row.Label) switch
                    {
                        "danger" => Color.FromRgb(181, 44, 65),
                        "warning" => Color.FromRgb(255, 245, 206),
                        "success" => Color.FromRgb(38, 118, 74),
                        _ => Color.FromRgb(36, 98, 142)
                    };
            }

            if (list.Count == 0)
            {
                Text("No data in this selection.");
            }
        }

        Heading(
            "City of Los Angeles | Bureau of Engineering",
            StyleNames.Heading3);

        Heading(document.Info.Title, StyleNames.Heading1);

        Text(
            "Generated " +
            ReportDisplay.Time(DateTime.UtcNow) +
            " Los Angeles time");

        if (!audit)
        {
            Text(
                $"Application: {model.Filter.Application ?? "All applications"}" +
                $" | {model.Filter.From} through {model.Filter.To}");

            Text(
                "Findings use the latest completed audit per application " +
                "in the selected dates; successful steps only.");

            Metrics(
                ("Applications", model.ApplicationsCount),
                ("Violations", model.ViolationCount),
                ("Needs review", model.ReviewCount),
                ("Tracked items", model.Statuses.Sum(x => x.Count)));

            Chart("Violation severity", model.Severity);

            Chart(
                "Current remediation status",
                model.Statuses);

            section.AddPageBreak();

            Chart(
                "Completed audits by month",
                model.Activity);

            Heading(
                "Latest completed audits (up to 10 applications)");

            Table(
                [
                    "Application / audit",
                    "Started (LA)",
                    "Violations",
                    "Review"
                ],
                model.Audits.Select(run => new[]
                {
                    $"{run.Application} / #{run.Id}",
                    ReportDisplay.Time(run.Started),
                    ReportDisplay.Number(run.Violations),
                    ReportDisplay.Number(run.Review)
                }),
                [2.6, 2.1, 1.2, 1.2]);
        }
        else
        {
            var run = model.Audit!;

            Text(
                $"Status: {run.Status}" +
                $" | Started: {ReportDisplay.Time(run.Started)}");

            Metrics(
                ("Scanned steps", run.SuccessfulSteps),
                ("Violations", run.Violations),
                ("Needs review", run.Review),
                ("Affected elements", run.Elements));

            Text(
                $"{run.SuccessfulSteps} of {run.Steps} recorded steps " +
                "scanned successfully.");

            Chart(
                "Violation severity",
                model.Findings
                    .Where(x => x.Type == "Violation")
                    .GroupBy(x => x.Impact)
                    .Select(x => new CountRow(x.Key, x.LongCount())));

            section.AddPageBreak();

            Heading("Page-by-page scan record");

            Table(
                [
                    "Step / page",
                    "Scanned (LA)",
                    "Result",
                    "Violations / review"
                ],
                model.Steps.Select(step => new[]
                {
                    $"{step.Number}. {step.Name}",
                    ReportDisplay.Time(step.Scanned),
                    step.Succeeded ? "Succeeded" : "Failed",
                    step.Succeeded
                        ? $"{step.Violations} / {step.Review}"
                        : "Not available"
                }),
                [2.6, 1.8, 1.1, 1.6]);

            var findingNumber = 0;

            foreach (var finding in model.Findings)
            {
                // Every finding begins on a fresh page.
                // Its technical evidence can continue across more pages.
                section.AddPageBreak();

                Heading(
                    $"Finding {++findingNumber} of {model.Findings.Count}",
                    StyleNames.Heading3);

                Heading(
                    string.IsNullOrWhiteSpace(finding.Help)
                        ? finding.Rule
                        : finding.Help);

                Text(
                    $"Step {finding.Step} / {finding.StepName}");

                var classificationBackground =
                    ReportDisplay.Tone(finding.Impact) switch
                    {
                        "danger" => Color.FromRgb(255, 235, 238),
                        "warning" => Color.FromRgb(255, 245, 206),
                        "success" => Color.FromRgb(233, 247, 237),
                        _ => Color.FromRgb(238, 244, 250)
                    };

                Panel(
                    "Priority and classification",
                    $"{ReportDisplay.Priority(finding.Impact)}  |  " +
                    $"{finding.Impact}  |  " +
                    $"{ReportDisplay.Status(finding.Type)}  |  " +
                    finding.Rule,
                    classificationBackground);

                Text(
                    $"WCAG level: {finding.Level ?? "Not recorded"}" +
                    $" | Tags: {finding.Tags}" +
                    $" | Affected elements: {finding.Elements}");

                Text(finding.Description);

                if (ReportDisplay.SafeLink(finding.HelpUrl) is { } url)
                {
                    Text("Rule guidance: " + url);
                }

                if (finding.Nodes.Count == 0)
                {
                    Text("No technical element details saved.");
                }

                for (var i = 0; i < finding.Nodes.Count; i++)
                {
                    var node = finding.Nodes[i];

                    Heading(
                        $"Affected element {i + 1}",
                        StyleNames.Heading3);

                    Panel(
                        "CSS Selector / Target",
                        node.Target,
                        Color.FromRgb(240, 244, 248),
                        code: true);

                    Panel(
                        "Why This Element Failed",
                        node.FailureSummary,
                        Color.FromRgb(248, 249, 251));

                    Panel(
                        "Element Fix Guidance",
                        node.Guidance,
                        Color.FromRgb(233, 247, 237));

                    Panel(
                        "HTML Snippet",
                        node.Html,
                        Color.FromRgb(240, 244, 248),
                        code: true);
                }
            }
        }

        var renderer = new PdfDocumentRenderer
        {
            Document = document
        };

        renderer.RenderDocument();

        using var stream = new MemoryStream();

        renderer.PdfDocument.Save(stream, false);

        return stream.ToArray();
    }

    private static string Wrap(string text)
    {
        var output = new System.Text.StringBuilder();
        var length = 0;

        foreach (var character in text)
        {
            if (char.IsControl(character) &&
                character is not '\n' and not '\r' and not '\t')
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                length = 0;
            }
            else if (++length > 65)
            {
                // Allow long HTML and selectors to wrap within margins.
                output.Append('\u200B');
                length = 1;
            }

            output.Append(character);
        }

        return output.ToString();
    }
}
