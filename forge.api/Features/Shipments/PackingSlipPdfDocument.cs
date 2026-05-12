using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Core.Entities;

namespace Forge.Api.Features.Shipments;

public class PackingSlipPdfDocument : IDocument
{
    private readonly Shipment _shipment;
    private readonly string _companyName;

    public PackingSlipPdfDocument(Shipment shipment, string companyName)
    {
        _shipment = shipment;
        _companyName = companyName;
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
                    right.Item().Text("PACKING SLIP").Bold().FontSize(24).FontColor(Colors.Teal.Darken2);
                    right.Item().Text($"#{_shipment.ShipmentNumber}").FontSize(12);
                });
            });

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Ship To:").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    if (_shipment.ShippingAddress != null)
                    {
                        var addr = _shipment.ShippingAddress;
                        left.Item().Text(_shipment.SalesOrder.Customer.CompanyName
                            ?? _shipment.SalesOrder.Customer.Name).SemiBold();
                        left.Item().Text(addr.Line1);
                        if (!string.IsNullOrEmpty(addr.Line2))
                            left.Item().Text(addr.Line2);
                        left.Item().Text($"{addr.City}, {addr.State} {addr.PostalCode}");
                        left.Item().Text(addr.Country);
                    }
                    else
                    {
                        left.Item().Text(_shipment.SalesOrder.Customer.CompanyName
                            ?? _shipment.SalesOrder.Customer.Name).SemiBold();
                    }
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text($"SO #: {_shipment.SalesOrder.OrderNumber}");
                    if (_shipment.ShippedDate.HasValue)
                        right.Item().Text($"Ship Date: {_shipment.ShippedDate.Value:MMM dd, yyyy}");
                    if (!string.IsNullOrEmpty(_shipment.Carrier))
                        right.Item().Text($"Carrier: {_shipment.Carrier}");
                    if (!string.IsNullOrEmpty(_shipment.TrackingNumber))
                        right.Item().Text($"Tracking: {_shipment.TrackingNumber}");
                    if (_shipment.Weight.HasValue)
                        right.Item().Text($"Weight: {_shipment.Weight.Value:F1} lbs");
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
                    cols.ConstantColumn(40);    // #
                    cols.RelativeColumn(4);     // Description
                    cols.ConstantColumn(80);    // Part #
                    cols.ConstantColumn(60);    // Qty
                });

                table.Header(header =>
                {
                    var headerStyle = TextStyle.Default.SemiBold().FontSize(9).FontColor(Colors.White);

                    header.Cell().Background(Colors.Teal.Darken2).Padding(5).Text("#").Style(headerStyle);
                    header.Cell().Background(Colors.Teal.Darken2).Padding(5).Text("Description").Style(headerStyle);
                    header.Cell().Background(Colors.Teal.Darken2).Padding(5).Text("Part #").Style(headerStyle);
                    header.Cell().Background(Colors.Teal.Darken2).Padding(5).AlignRight().Text("Qty").Style(headerStyle);
                });

                var lines = _shipment.Lines.OrderBy(l => l.Id).ToList();
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    var description = line.SalesOrderLine?.Description ?? line.Part?.Description ?? "—";
                    var partNumber = line.SalesOrderLine?.Part?.PartNumber ?? line.Part?.PartNumber ?? "—";

                    table.Cell().Background(bg).Padding(5).Text((i + 1).ToString());
                    table.Cell().Background(bg).Padding(5).Text(description);
                    table.Cell().Background(bg).Padding(5).Text(partNumber);
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.Quantity.ToString());
                }
            });

            col.Item().PaddingTop(20).Row(row =>
            {
                row.RelativeItem().Column(summary =>
                {
                    summary.Item().Text($"Total Items: {_shipment.Lines.Sum(l => l.Quantity)}").SemiBold();
                    summary.Item().Text($"Total Lines: {_shipment.Lines.Count}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });

            if (!string.IsNullOrWhiteSpace(_shipment.Notes))
            {
                col.Item().PaddingTop(20).Column(notes =>
                {
                    notes.Item().Text("Notes").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    notes.Item().PaddingTop(4).Text(_shipment.Notes);
                });
            }

            col.Item().PaddingTop(40).Column(sig =>
            {
                sig.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().LineHorizontal(1).LineColor(Colors.Grey.Medium);
                        left.Item().PaddingTop(4).Text("Received By").FontSize(9).FontColor(Colors.Grey.Darken1);
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
