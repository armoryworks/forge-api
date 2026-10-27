using System.Globalization;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Api.Features.VendorCredits;
using Forge.Api.Services;
using Forge.Core.Entities;

namespace Forge.Api.Features.VendorReturns;

/// <summary>The return-to-vendor packing document: what goes back, from which lot and bin, and the credit expected.</summary>
public class VendorReturnPdfDocument(
    VendorReturn rtv, string? companyName, MoneyFormatter money, VendorDocumentPdfLabels labels) : IDocument
{
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginHorizontal(50);
            page.MarginVertical(40);
            page.DefaultTextStyle(x => x.WithScriptFallbacks().FontSize(10));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
                text.Span($"{labels["page"]} ");
                text.CurrentPageNumber();
                text.Span($" {labels["of"]} ");
                text.TotalPages();
            });
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
                    if (companyName is not null)
                        left.Item().Text(companyName).Bold().FontSize(18);
                });
                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text(labels["rtv"]).Bold().FontSize(20).FontColor(Colors.Blue.Darken2);
                    right.Item().Text(rtv.ReturnNumber).FontSize(12);
                });
            });

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(labels["shipTo"]).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    left.Item().Text(rtv.Vendor.CompanyName).SemiBold();
                    if (!string.IsNullOrWhiteSpace(rtv.Vendor.Address))
                        left.Item().Text(rtv.Vendor.Address).FontSize(9);
                });
                row.RelativeItem().AlignRight().Column(right =>
                {
                    if (rtv.NonConformance is not null)
                        right.Item().Text($"{labels["ncr"]} {rtv.NonConformance.NcrNumber}");
                    if (rtv.PurchaseOrder is not null)
                        right.Item().Text($"{labels["po"]} {rtv.PurchaseOrder.PONumber}");
                    if (!string.IsNullOrWhiteSpace(rtv.RmaNumber))
                        right.Item().Text($"{labels["rma"]} {rtv.RmaNumber}");
                    if (rtv.ShipDate is { } shipDate)
                        right.Item().Text($"{labels["shipDate"]} {shipDate.ToString(labels.DateFormat, CultureInfo.InvariantCulture)}");
                    if (!string.IsNullOrWhiteSpace(rtv.Carrier))
                        right.Item().Text($"{labels["carrier"]} {rtv.Carrier}");
                    if (!string.IsNullOrWhiteSpace(rtv.TrackingNumber))
                        right.Item().Text($"{labels["tracking"]} {rtv.TrackingNumber}");
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
                    cols.ConstantColumn(30);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(1);
                    cols.RelativeColumn(1);
                    cols.ConstantColumn(55);
                    cols.ConstantColumn(75);
                });

                table.Header(header =>
                {
                    var headerStyle = TextStyle.Default.SemiBold().FontSize(9).FontColor(Colors.White);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text(labels["number"]).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text(labels["part"]).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text(labels["lot"]).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text(labels["location"]).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text(labels["qty"]).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text(labels["unitPrice"]).Style(headerStyle);
                });

                var lines = rtv.Lines.OrderBy(l => l.LineNumber).ToList();
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    table.Cell().Background(bg).Padding(5).Text(line.LineNumber.ToString(CultureInfo.InvariantCulture));
                    table.Cell().Background(bg).Padding(5).Text(line.Part.Description is { Length: > 0 } d
                        ? $"{line.Part.PartNumber} — {d}"
                        : line.Part.PartNumber);
                    table.Cell().Background(bg).Padding(5).Text(line.LotNumber ?? string.Empty);
                    table.Cell().Background(bg).Padding(5).Text(line.Location?.Name ?? string.Empty);
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(MoneyFormat.Quantity(line.Quantity));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(money.Format(line.UnitPrice));
                }
            });

            col.Item().PaddingTop(10).AlignRight().Width(240).Row(row =>
            {
                row.RelativeItem().Text(labels["expectedCredit"]).Bold();
                row.ConstantItem(100).AlignRight().Text(money.Format(rtv.ExpectedCredit)).Bold();
            });

            if (!string.IsNullOrWhiteSpace(rtv.Notes))
            {
                col.Item().PaddingTop(20).Column(notes =>
                {
                    notes.Item().Text(labels["notes"]).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    notes.Item().PaddingTop(4).Text(rtv.Notes);
                });
            }

            col.Item().PaddingTop(40).Text(labels["packedBy"]);
            col.Item().PaddingTop(20).Text(labels["receivedBy"]);
        });
    }
}
