using CityWebsiteAuditDashboard.Reporting.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace CityWebsiteAuditDashboard.Reporting.Services;

// Local font files only; report generation never visits finding URLs or starts a browser.
public sealed class ReportFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string family, bool bold, bool italic) => new(bold ? "ReportBold" : "ReportRegular");
    public byte[] GetFont(string face)
    {
        var bold = face == "ReportBold";
        var path = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), bold ? "arialbd.ttf" : "arial.ttf")
            : "/usr/share/fonts/truetype/dejavu/" + (bold ? "DejaVuSans-Bold.ttf" : "DejaVuSans.ttf");
        return File.ReadAllBytes(path);
    }
}

public static class ReportPdf
{
    public static byte[] Create(ReportPage model, bool audit)
    {
        var document = new Document();
        document.Info.Title = audit ? $"{model.Audit!.Application} - Audit {model.Audit.Id}" : "BOE Accessibility Overview";
        document.Info.Author = "City of Los Angeles - Bureau of Engineering";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Report"; normal.Font.Size = 10;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);
        foreach (var style in new[] { StyleNames.Heading1, StyleNames.Heading2, StyleNames.Heading3 })
        {
            document.Styles[style]!.Font.Name = "Report";
            document.Styles[style]!.Font.Color = Color.FromRgb(18, 51, 74);
            document.Styles[style]!.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
        }
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromInch(.65);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromInch(.7);
        var footer = section.Footers.Primary.AddParagraph("BOE Accessibility Reports | Page ");
        footer.AddPageField(); footer.Format.Font.Size = 8;
        void Text(string? value) => section.AddParagraph(Wrap(value ?? "Not recorded"));
        void Heading(string text, string style = StyleNames.Heading2) => section.AddParagraph(text, style);
        Heading("City of Los Angeles | Bureau of Engineering", StyleNames.Heading3);
        Heading(document.Info.Title, StyleNames.Heading1);
        Text("Generated " + ReportDisplay.Time(DateTime.UtcNow) + " Los Angeles time");
        void Chart(string title, IEnumerable<CountRow> rows)
        {
            Heading(title);
            var list = rows.ToList(); var max = Math.Max(1, list.Select(x => x.Count).DefaultIfEmpty().Max());
            foreach (var row in list)
            {
                var paragraph = section.AddParagraph($"{row.Label}: {ReportDisplay.Number(row.Count)}");
                paragraph.Format.KeepWithNext = true;
                var bar = section.AddParagraph(" ");
                bar.Format.Font.Size = 5;
                bar.Format.RightIndent = Unit.FromInch(7.1 * (1 - Math.Max(.008, (double)row.Count / max)));
                bar.Format.Shading.Color = ReportDisplay.Tone(row.Label) switch
                { "danger" => Color.FromRgb(181,44,65), "warning" => Color.FromRgb(151,112,0), "success" => Color.FromRgb(38,118,74), _ => Color.FromRgb(36,98,142) };
            }
            if (list.Count == 0) Text("No data in this selection.");
        }
        if (!audit)
        {
            Text($"Application: {model.Filter.Application ?? "All applications"} | {model.Filter.From} through {model.Filter.To}");
            Text("Findings use the latest completed audit per application in the selected dates; successful steps only.");
            Text($"Applications: {model.ApplicationsCount} | Violations: {model.ViolationCount} | Needs review: {model.ReviewCount}");
            Chart("Violation severity", model.Severity);
            Chart("Current remediation status", model.Statuses);
            Chart("Completed audits by month", model.Activity);
            Heading("Latest completed audits (up to 10 applications)");
            foreach (var run in model.Audits) Text($"#{run.Id} {run.Application} | {ReportDisplay.Time(run.Started)} | {run.Violations} violations | {run.Review} review findings");
        }
        else
        {
            var run = model.Audit!;
            Text($"Status: {run.Status} | Started: {ReportDisplay.Time(run.Started)}");
            Text($"Successful steps: {run.SuccessfulSteps}/{run.Steps} | Violations: {run.Violations} | Review: {run.Review} | Affected elements: {run.Elements}");
            Chart("Violation severity", model.Findings.Where(x => x.Type == "Violation").GroupBy(x => x.Impact).Select(x => new CountRow(x.Key, x.LongCount())));
            Heading("Page-by-page scan record");
            foreach (var step in model.Steps) Text($"{step.Number}. {step.Name}: {(step.Succeeded ? $"{step.Violations} violations, {step.Review} review findings" : "Scan failed")}");
            Heading("All findings and technical affected elements");
            foreach (var finding in model.Findings)
            {
                Heading($"Step {finding.Step}: {finding.Help}", StyleNames.Heading3);
                Text($"{ReportDisplay.Status(finding.Type)} | {finding.Impact} | {ReportDisplay.Priority(finding.Impact)} | {finding.Rule}");
                Text($"WCAG level: {finding.Level ?? "Not recorded"} | Tags: {finding.Tags} | Affected elements: {finding.Elements}");
                Text(finding.Description);
                if (ReportDisplay.SafeLink(finding.HelpUrl) is { } url) Text("Rule guidance: " + url);
                if (finding.Nodes.Count == 0) Text("No technical element details saved.");
                for (var i = 0; i < finding.Nodes.Count; i++)
                {
                    var node = finding.Nodes[i];
                    Heading($"Affected element {i + 1}", StyleNames.Heading3);
                    Text("CSS selector / target: " + node.Target);
                    Text("Why this element failed: " + (node.FailureSummary ?? "Not recorded"));
                    Text("Element fix guidance: " + (node.Guidance ?? "Not recorded"));
                    Text("HTML snippet: " + (node.Html ?? "Not recorded"));
                }
            }
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream(); renderer.PdfDocument.Save(stream, false); return stream.ToArray();
    }
    // Keep long selectors/HTML tokens inside the PDF margins without removing evidence.
    private static string Wrap(string text)
    {
        var output = new System.Text.StringBuilder(); var length = 0;
        foreach (var c in text)
        {
            if (char.IsControl(c) && c is not '\n' and not '\r' and not '\t') continue;
            if (char.IsWhiteSpace(c)) length = 0;
            else if (++length > 65) { output.Append('\u200B'); length = 1; }
            output.Append(c);
        }
        return output.ToString();
    }
}
