using Forge.Api.Services.Pdf;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public static class ReportPdfDocument
{
    private const string Styles = """
        @page landscape { margin: 30pt 30pt 44pt; }
        body { font-size: 8pt; }
        .head .report-name { font-size: 16pt; font-weight: 700; padding-right: 10pt; }
        .head .meta { width: 200pt; flex: 0 0 200pt; font-size: 8pt; color: #757575; }
        .report-rule { border: 0; border-top: 1pt solid #bdbdbd; margin: 6pt 0; }
        .empty { text-align: center; padding-top: 40pt; font-size: 12pt; color: #757575; }
        table.lines.report { table-layout: fixed; }
        table.lines.report thead tr { background: #00796b; }
        table.lines.report th { font-size: 7pt; padding: 4pt; }
        table.lines.report td { font-size: 7pt; padding: 4pt; overflow-wrap: break-word; }
        """;

    private const int MaxColumns = 15;

    public static HtmlPdfDocument Build(RunReportResponseModel result, string reportName, DateTimeOffset generatedAt)
        => BuildParts(result, reportName, generatedAt, int.MaxValue)[0];

    public static IReadOnlyList<HtmlPdfDocument> BuildParts(
        RunReportResponseModel result, string reportName, DateTimeOffset generatedAt, int cellsPerPart = PdfParts.CellsPerPart)
    {
        var columns = result.Columns.Take(MaxColumns).ToList();
        var parts = PdfParts.Split(result.Rows, PdfParts.RowsPerPart(columns.Count, cellsPerPart));
        var documents = new List<HtmlPdfDocument>(parts.Count);
        var printed = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var range = parts.Count == 1
                ? Html.Empty
                : Html.Of($"<div>Part {i + 1} of {parts.Count}: records {printed + 1:N0} to {printed + parts[i].Count:N0}</div>");
            var body = Html.Of($"{Header(result, reportName, generatedAt, range)}{Content(columns, parts[i])}");
            documents.Add(PdfHtml.Document(
                reportName, body, PdfParts.Label(reportName, i + 1, parts.Count), landscape: true, styles: Styles));
            printed += parts[i].Count;
        }

        return documents;
    }

    private static Html Header(RunReportResponseModel result, string reportName, DateTimeOffset generatedAt, Html range)
        => Html.Of($"""
            <div class="head">
              <div class="report-name">{reportName}</div>
              <div class="meta right">
                <div>Generated: {generatedAt:MMM dd, yyyy HH:mm} UTC</div>
                <div>{result.TotalCount} records</div>
                {range}
              </div>
            </div>
            <hr class="report-rule">
            """);

    private static Html Content(IReadOnlyList<string> columns, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0)
            return Html.Of($"<div class=\"empty\">No data to display.</div>");

        var headings = columns.Select(c => Html.Of($"<th>{c}</th>"));
        var lines = rows.Select(row => Html.Of($"""
            <tr>{columns.Select(c => Html.Of($"<td>{FormatValue(row.TryGetValue(c, out var v) ? v : null)}</td>"))}</tr>
            """));

        return Html.Of($"""
            <table class="lines report">
              <thead><tr>{headings}</tr></thead>
              <tbody>{lines}</tbody>
            </table>
            """);
    }

    private static string FormatValue(object? value)
    {
        if (value is null) return "—";
        if (value is DateTime dt) return dt.ToString("MM/dd/yyyy");
        if (value is DateTimeOffset dto) return dto.ToString("MM/dd/yyyy");
        if (value is decimal dec) return dec.ToString("N2");
        if (value is double dbl) return dbl.ToString("N2");
        if (value is bool b) return b ? "Yes" : "No";
        return value.ToString() ?? "—";
    }
}
