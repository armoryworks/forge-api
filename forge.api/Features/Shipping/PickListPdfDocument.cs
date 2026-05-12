using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Core.Entities;

namespace Forge.Api.Features.Shipping;

public class PickListPdfDocument : IDocument
{
    private readonly PickWave _wave;
    private readonly string _companyName;
    private readonly DateTimeOffset _generatedAt;

    public PickListPdfDocument(PickWave wave, string companyName, DateTimeOffset generatedAt)
    {
        _wave = wave;
        _companyName = companyName;
        _generatedAt = generatedAt;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginHorizontal(50);
            page.MarginVertical(40);
            page.DefaultTextStyle(x => x.FontSize(10));

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
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(_companyName).Bold().FontSize(18);
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text("PICK LIST").Bold().FontSize(24).FontColor(Colors.Blue.Darken2);
                    right.Item().Text($"Wave #{_wave.WaveNumber}").FontSize(12);
                });
            });

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text($"Status: {_wave.Status}").FontSize(9);
                    left.Item().Text($"Strategy: {_wave.Strategy}").FontSize(9);
                    if (_wave.ReleasedAt.HasValue)
                        left.Item().Text($"Released: {_wave.ReleasedAt.Value:MMM dd, yyyy HH:mm}").FontSize(9);
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text($"Total Lines: {_wave.TotalLines}").FontSize(9);
                    right.Item().Text($"Picked: {_wave.PickedLines}").FontSize(9);
                    right.Item().Text($"Date: {_generatedAt:MMM dd, yyyy}").FontSize(9);
                });
            });

            col.Item().PaddingTop(10);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(35);    // #
                    cols.ConstantColumn(90);    // Part #
                    cols.RelativeColumn(3);     // Description
                    cols.RelativeColumn(2);     // Location / Bin
                    cols.ConstantColumn(60);    // Req Qty
                    cols.ConstantColumn(60);    // Picked Qty
                    cols.ConstantColumn(60);    // Status
                });

                var headerStyle = TextStyle.Default.SemiBold().FontSize(9).FontColor(Colors.White);

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("#").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Part #").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Description").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Location").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Req").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Picked").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Status").Style(headerStyle);
                });

                var lines = _wave.Lines.OrderBy(l => l.SortOrder).ToList();
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    var location = !string.IsNullOrEmpty(line.BinPath) ? line.BinPath : line.FromLocation?.Name ?? "—";

                    table.Cell().Background(bg).Padding(5).Text((i + 1).ToString());
                    table.Cell().Background(bg).Padding(5).Text(line.Part?.PartNumber ?? "—");
                    table.Cell().Background(bg).Padding(5).Text(line.Part?.Description ?? "—");
                    table.Cell().Background(bg).Padding(5).Text(location);
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.RequestedQuantity.ToString("G"));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.PickedQuantity.ToString("G"));
                    table.Cell().Background(bg).Padding(5).Text(line.Status.ToString());
                }
            });

            col.Item().PaddingTop(20).Row(row =>
            {
                row.RelativeItem().Column(summary =>
                {
                    summary.Item().Text($"Total Items: {_wave.Lines.Sum(l => l.RequestedQuantity):G}").SemiBold();
                    summary.Item().Text($"Total Lines: {_wave.Lines.Count}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });

            if (!string.IsNullOrWhiteSpace(_wave.Notes))
            {
                col.Item().PaddingTop(20).Column(notes =>
                {
                    notes.Item().Text("Notes").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    notes.Item().PaddingTop(4).Text(_wave.Notes);
                });
            }

            col.Item().PaddingTop(40).Column(sig =>
            {
                sig.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().LineHorizontal(1).LineColor(Colors.Grey.Medium);
                        left.Item().PaddingTop(4).Text("Picked By").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });

                    row.ConstantItem(40);

                    row.RelativeItem().Column(right =>
                    {
                        right.Item().LineHorizontal(1).LineColor(Colors.Grey.Medium);
                        right.Item().PaddingTop(4).Text("Date").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        });
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
