using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Core.Entities;

namespace Forge.Api.Features.Shipments;

public class BillOfLadingPdfDocument : IDocument
{
    private readonly Shipment _shipment;
    private readonly string _companyName;
    private readonly string? _companyAddress;
    private readonly string? _companyPhone;

    public BillOfLadingPdfDocument(
        Shipment shipment,
        string companyName,
        string? companyAddress = null,
        string? companyPhone = null)
    {
        _shipment = shipment;
        _companyName = companyName;
        _companyAddress = companyAddress;
        _companyPhone = companyPhone;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginHorizontal(40);
            page.MarginVertical(30);
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Text("BILL OF LADING").Bold().FontSize(20);
            col.Item().AlignCenter().Text("SHORT FORM — NOT NEGOTIABLE").FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(6).LineHorizontal(2).LineColor(Colors.Black);
            col.Item().PaddingTop(8);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            // Shipper / Consignee / Carrier block
            col.Item().Row(row =>
            {
                row.RelativeItem().Border(1).Padding(8).Column(shipper =>
                {
                    shipper.Item().Text("SHIPPER (From):").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    shipper.Item().PaddingTop(4).Text(_companyName).SemiBold();
                    if (!string.IsNullOrEmpty(_companyAddress))
                        shipper.Item().Text(_companyAddress);
                    if (!string.IsNullOrEmpty(_companyPhone))
                        shipper.Item().Text($"Tel: {_companyPhone}");
                });

                row.ConstantItem(1);

                row.RelativeItem().Border(1).Padding(8).Column(consignee =>
                {
                    consignee.Item().Text("CONSIGNEE (To):").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    var customer = _shipment.SalesOrder?.Customer;
                    if (customer != null)
                    {
                        consignee.Item().PaddingTop(4).Text(customer.CompanyName ?? customer.Name).SemiBold();
                    }
                    if (_shipment.ShippingAddress != null)
                    {
                        var addr = _shipment.ShippingAddress;
                        consignee.Item().Text(addr.Line1);
                        if (!string.IsNullOrEmpty(addr.Line2))
                            consignee.Item().Text(addr.Line2);
                        consignee.Item().Text($"{addr.City}, {addr.State} {addr.PostalCode}");
                        consignee.Item().Text(addr.Country);
                    }
                });
            });

            col.Item().PaddingTop(4);

            // Reference info
            col.Item().Border(1).Padding(8).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("CARRIER:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    left.Item().Text(_shipment.Carrier ?? "—").SemiBold();
                });
                row.RelativeItem().Column(mid =>
                {
                    mid.Item().Text("B/L NUMBER:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    mid.Item().Text($"BOL-{_shipment.ShipmentNumber}").SemiBold();
                });
                row.RelativeItem().Column(right =>
                {
                    right.Item().Text("DATE:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    right.Item().Text((_shipment.ShippedDate ?? DateTimeOffset.UtcNow).ToString("MMM dd, yyyy")).SemiBold();
                });
            });

            col.Item().PaddingTop(4);

            // Tracking / SO / PRO
            col.Item().Border(1).Padding(8).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("SO NUMBER:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    left.Item().Text(_shipment.SalesOrder?.OrderNumber ?? "—");
                });
                row.RelativeItem().Column(mid =>
                {
                    mid.Item().Text("TRACKING #:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    mid.Item().Text(_shipment.TrackingNumber ?? "—");
                });
                row.RelativeItem().Column(right =>
                {
                    right.Item().Text("SHIPMENT #:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    right.Item().Text(_shipment.ShipmentNumber);
                });
            });

            col.Item().PaddingTop(8);

            // Items table
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(35);    // #
                    cols.RelativeColumn(3);     // Description
                    cols.ConstantColumn(90);    // Part #
                    cols.ConstantColumn(55);    // Qty
                    cols.ConstantColumn(55);    // Weight
                    cols.ConstantColumn(80);    // Packaging
                });

                var headerStyle = TextStyle.Default.SemiBold().FontSize(8).FontColor(Colors.White);

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Black).Padding(5).Text("#").Style(headerStyle);
                    header.Cell().Background(Colors.Black).Padding(5).Text("DESCRIPTION OF ARTICLES").Style(headerStyle);
                    header.Cell().Background(Colors.Black).Padding(5).Text("PART #").Style(headerStyle);
                    header.Cell().Background(Colors.Black).Padding(5).AlignRight().Text("QTY").Style(headerStyle);
                    header.Cell().Background(Colors.Black).Padding(5).AlignRight().Text("WEIGHT").Style(headerStyle);
                    header.Cell().Background(Colors.Black).Padding(5).Text("PACKAGING").Style(headerStyle);
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
                    table.Cell().Background(bg).Padding(5).AlignRight().Text("—");
                    table.Cell().Background(bg).Padding(5).Text("Carton");
                }
            });

            // Packages summary
            if (_shipment.Packages.Count > 0)
            {
                col.Item().PaddingTop(8).Text("PACKAGES").Bold().FontSize(9);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(35);
                        cols.RelativeColumn(2);
                        cols.ConstantColumn(70);
                        cols.ConstantColumn(120);
                    });

                    var headerStyle = TextStyle.Default.SemiBold().FontSize(8).FontColor(Colors.White);
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Darken3).Padding(4).Text("#").Style(headerStyle);
                        header.Cell().Background(Colors.Grey.Darken3).Padding(4).Text("TRACKING").Style(headerStyle);
                        header.Cell().Background(Colors.Grey.Darken3).Padding(4).AlignRight().Text("WEIGHT").Style(headerStyle);
                        header.Cell().Background(Colors.Grey.Darken3).Padding(4).Text("DIMENSIONS").Style(headerStyle);
                    });

                    var packages = _shipment.Packages.ToList();
                    for (var i = 0; i < packages.Count; i++)
                    {
                        var pkg = packages[i];
                        var dims = pkg.Length.HasValue && pkg.Width.HasValue && pkg.Height.HasValue
                            ? $"{pkg.Length:G} × {pkg.Width:G} × {pkg.Height:G} in"
                            : "—";

                        table.Cell().Padding(4).Text((i + 1).ToString());
                        table.Cell().Padding(4).Text(pkg.TrackingNumber ?? "—");
                        table.Cell().Padding(4).AlignRight().Text(pkg.Weight.HasValue ? $"{pkg.Weight.Value:F1} lbs" : "—");
                        table.Cell().Padding(4).Text(dims);
                    }
                });
            }

            // Weight summary
            col.Item().PaddingTop(12).Border(1).Padding(8).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("TOTAL PIECES:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    left.Item().Text($"{_shipment.Lines.Sum(l => l.Quantity)}");
                });
                row.RelativeItem().Column(mid =>
                {
                    mid.Item().Text("TOTAL PACKAGES:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    mid.Item().Text($"{(_shipment.Packages.Count > 0 ? _shipment.Packages.Count : _shipment.Lines.Count)}");
                });
                row.RelativeItem().Column(right =>
                {
                    right.Item().Text("TOTAL WEIGHT:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    var totalWeight = _shipment.Weight ?? _shipment.Packages.Where(p => p.Weight.HasValue).Sum(p => p.Weight!.Value);
                    right.Item().Text(totalWeight > 0 ? $"{totalWeight:F1} lbs" : "—");
                });
            });

            // Notes
            if (!string.IsNullOrWhiteSpace(_shipment.Notes))
            {
                col.Item().PaddingTop(12).Column(notes =>
                {
                    notes.Item().Text("SPECIAL INSTRUCTIONS:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                    notes.Item().PaddingTop(4).Text(_shipment.Notes);
                });
            }

            // Signature block
            col.Item().PaddingTop(30).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("SHIPPER SIGNATURE:").FontSize(8).FontColor(Colors.Grey.Darken2);
                    left.Item().PaddingTop(20).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    left.Item().PaddingTop(4).Text("Signature / Date").FontSize(8).FontColor(Colors.Grey.Medium);
                });

                row.ConstantItem(30);

                row.RelativeItem().Column(right =>
                {
                    right.Item().Text("CARRIER SIGNATURE:").FontSize(8).FontColor(Colors.Grey.Darken2);
                    right.Item().PaddingTop(20).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    right.Item().PaddingTop(4).Text("Signature / Date").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });

            col.Item().PaddingTop(20).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("RECEIVED BY:").FontSize(8).FontColor(Colors.Grey.Darken2);
                    left.Item().PaddingTop(20).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    left.Item().PaddingTop(4).Text("Print Name / Date").FontSize(8).FontColor(Colors.Grey.Medium);
                });

                row.ConstantItem(30);

                row.RelativeItem().Column(right =>
                {
                    right.Item().Text("RECEIVER SIGNATURE:").FontSize(8).FontColor(Colors.Grey.Darken2);
                    right.Item().PaddingTop(20).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    right.Item().PaddingTop(4).Text("Signature / Date").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            col.Item().PaddingTop(4).AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Medium));
                text.Span("This is to certify that the above-named materials are properly classified, described, packaged, marked, and labeled, and are in proper condition for transportation. ");
            });
            col.Item().PaddingTop(4).AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }
}
