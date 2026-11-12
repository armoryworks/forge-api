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
        .shortened { margin-bottom: 6pt; font-size: 9pt; font-weight: 600; }
        table.lines.report { table-layout: fixed; }
        table.lines.report thead tr { background: #00796b; }
        table.lines.report th { font-size: 7pt; padding: 4pt; }
        table.lines.report td { font-size: 7pt; padding: 4pt; overflow-wrap: break-word; }
        """;

    private const int MaxColumns = 15;
    private const int MaxCells = 12_000;

    public static HtmlPdfDocument Build(RunReportResponseModel result, string reportName, DateTimeOffset generatedAt)
    {
        var body = Html.Of($"{Header(result, reportName, generatedAt)}{Content(result)}");
        return PdfHtml.Document(reportName, body, reportName, landscape: true, styles: Styles);
    }

    private static Html Header(RunReportResponseModel result, string reportName, DateTimeOffset generatedAt)
        => Html.Of($"""
            <div class="head">
              <div class="report-name">{reportName}</div>
              <div class="meta right">
                <div>Generated: {generatedAt:MMM dd, yyyy HH:mm} UTC</div>
                <div>{result.TotalCount} records</div>
              </div>
            </div>
            <hr class="report-rule">
            """);

    private static Html Content(RunReportResponseModel result)
    {
        if (result.Rows.Count == 0)
            return Html.Of($"<div class=\"empty\">No data to display.</div>");

        var columns = result.Columns.Take(MaxColumns).ToList();
        var rowLimit = Math.Max(1, MaxCells / Math.Max(1, columns.Count));
        var headings = columns.Select(c => Html.Of($"<th>{c}</th>"));
        var rows = result.Rows.Take(rowLimit).Select(row => Html.Of($"""
            <tr>{columns.Select(c => Html.Of($"<td>{FormatValue(row.TryGetValue(c, out var v) ? v : null)}</td>"))}</tr>
            """));
        var shortened = result.Rows.Count > rowLimit
            ? Html.Of($"<div class=\"shortened\">Showing the first {rowLimit:N0} of {Math.Max(result.TotalCount, result.Rows.Count):N0} records. Export to CSV or Excel for the full list.</div>")
            : Html.Empty;

        return Html.Of($"""
            {shortened}
            <table class="lines report">
              <thead><tr>{headings}</tr></thead>
              <tbody>{rows}</tbody>
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
