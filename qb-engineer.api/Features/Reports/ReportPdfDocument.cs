using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using QBEngineer.Core.Models;

namespace QBEngineer.Api.Features.Reports;

public class ReportPdfDocument : IDocument
{
    private readonly RunReportResponseModel _result;
    private readonly string _reportName;
    private readonly DateTimeOffset _generatedAt;

    public ReportPdfDocument(RunReportResponseModel result, string reportName, DateTimeOffset generatedAt)
    {
        _result = result;
        _reportName = reportName;
        _generatedAt = generatedAt;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter.Landscape());
            page.MarginHorizontal(30);
            page.MarginVertical(30);
            page.DefaultTextStyle(x => x.FontSize(8));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(_reportName).Bold().FontSize(16);
                row.ConstantItem(200).AlignRight().Column(right =>
                {
                    right.Item().Text($"Generated: {_generatedAt:MMM dd, yyyy HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Darken1);
                    right.Item().Text($"{_result.TotalCount} records").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
            col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            col.Item().PaddingTop(6);
        });
    }

    private void ComposeContent(IContainer container)
    {
        if (_result.Rows.Count == 0)
        {
            container.AlignCenter().PaddingTop(40).Text("No data to display.").FontSize(12).FontColor(Colors.Grey.Darken1);
            return;
        }

        container.Table(table =>
        {
            var columns = _result.Columns;
            var maxColumns = Math.Min(columns.Length, 15);

            table.ColumnsDefinition(cols =>
            {
                for (var i = 0; i < maxColumns; i++)
                    cols.RelativeColumn();
            });

            var headerStyle = TextStyle.Default.SemiBold().FontSize(7).FontColor(Colors.White);

            table.Header(header =>
            {
                for (var i = 0; i < maxColumns; i++)
                {
                    header.Cell().Background(Colors.Teal.Darken2).Padding(4).Text(columns[i]).Style(headerStyle);
                }
            });

            for (var r = 0; r < _result.Rows.Count; r++)
            {
                var row = _result.Rows[r];
                var bg = r % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                for (var c = 0; c < maxColumns; c++)
                {
                    var value = row.TryGetValue(columns[c], out var v) ? v : null;
                    var text = FormatValue(value);
                    table.Cell().Background(bg).Padding(4).Text(text).FontSize(7);
                }
            }
        });
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

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
            text.Span("Page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
        });
    }
}
