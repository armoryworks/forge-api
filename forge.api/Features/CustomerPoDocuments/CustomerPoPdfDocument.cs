using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Core.Entities;

namespace Forge.Api.Features.CustomerPoDocuments;

/// <summary>
/// S4a — internally-generated customer purchase-order record. Structural
/// clone of <see cref="Forge.Api.Features.Invoices.InvoicePdfDocument"/>.
/// The document renders LIVE from the linked sales order (the entity passed
/// in must have SalesOrder → Customer / Lines→Part / ShippingAddress loaded),
/// and is clearly labeled as an internal record referencing the Forge SO
/// number — it is NOT a customer-supplied document.
/// </summary>
public class CustomerPoPdfDocument : IDocument
{
    private readonly CustomerPoDocument _document;
    private readonly SalesOrder _order;
    private readonly string _companyName;

    public CustomerPoPdfDocument(CustomerPoDocument document, string companyName)
    {
        _document = document;
        _order = document.SalesOrder;
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
                    right.Item().Text("PURCHASE ORDER").Bold().FontSize(24).FontColor(Colors.Blue.Darken2);
                    right.Item().Text($"#{_document.DocumentNumber}").FontSize(12);
                    right.Item().Text("Internal record — auto-generated").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });

            col.Item().PaddingTop(6).Background(Colors.Grey.Lighten4).Padding(6)
                .Text($"Internally-generated purchase order record for Forge sales order {_order.OrderNumber}. " +
                      "Contents render live from the current sales order.")
                .FontSize(8).Italic().FontColor(Colors.Grey.Darken2);

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Customer:").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    left.Item().Text(_order.Customer.CompanyName ?? _order.Customer.Name).SemiBold();
                    if (!string.IsNullOrEmpty(_order.Customer.Email))
                        left.Item().Text(_order.Customer.Email).FontSize(9);
                    if (!string.IsNullOrEmpty(_order.Customer.Phone))
                        left.Item().Text(_order.Customer.Phone).FontSize(9);
                    if (_order.ShippingAddress is not null)
                    {
                        left.Item().PaddingTop(4).Text("Ship To:").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                        left.Item().Text(_order.ShippingAddress.Line1).FontSize(9);
                        if (!string.IsNullOrWhiteSpace(_order.ShippingAddress.Line2))
                            left.Item().Text(_order.ShippingAddress.Line2!).FontSize(9);
                        left.Item().Text($"{_order.ShippingAddress.City}, {_order.ShippingAddress.State} {_order.ShippingAddress.PostalCode}").FontSize(9);
                    }
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text($"Generated: {_document.GeneratedAt:MM/dd/yyyy}");
                    right.Item().Text($"SO #: {_order.OrderNumber}");
                    right.Item().Text($"SO Status: {_order.Status}");
                    if (!string.IsNullOrEmpty(_order.CustomerPO))
                        right.Item().Text($"Customer PO Ref: {_order.CustomerPO}");
                    if (!string.IsNullOrEmpty(_document.GeneratedFromQuote?.QuoteNumber))
                        right.Item().Text($"Quote #: {_document.GeneratedFromQuote.QuoteNumber}");
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
                    cols.RelativeColumn(3);     // Description
                    cols.ConstantColumn(80);    // Part #
                    cols.ConstantColumn(60);    // Qty
                    cols.ConstantColumn(80);    // Unit Price
                    cols.ConstantColumn(90);    // Total
                });

                table.Header(header =>
                {
                    var headerStyle = TextStyle.Default.SemiBold().FontSize(9).FontColor(Colors.White);

                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("#").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Description").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Part #").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Qty").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Unit Price").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Total").Style(headerStyle);
                });

                var lines = _order.Lines.OrderBy(l => l.LineNumber).ToList();
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                    table.Cell().Background(bg).Padding(5).Text(line.LineNumber.ToString());
                    table.Cell().Background(bg).Padding(5).Text(line.Description);
                    table.Cell().Background(bg).Padding(5).Text(line.Part?.PartNumber ?? "—");
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.Quantity.ToString("0.####"));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.UnitPrice.ToString("C"));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.LineTotal.ToString("C"));
                }
            });

            col.Item().PaddingTop(10).AlignRight().Width(200).Column(totals =>
            {
                totals.Item().Row(row =>
                {
                    row.RelativeItem().Text("Subtotal:").SemiBold();
                    row.ConstantItem(100).AlignRight().Text(_order.Subtotal.ToString("C"));
                });

                if (_order.TaxRate > 0)
                {
                    totals.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Tax ({_order.TaxRate:P0}):").SemiBold();
                        row.ConstantItem(100).AlignRight().Text(_order.TaxAmount.ToString("C"));
                    });
                }

                totals.Item().PaddingTop(4).LineHorizontal(1);

                totals.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text("Total:").Bold().FontSize(12);
                    row.ConstantItem(100).AlignRight().Text(_order.Total.ToString("C")).Bold().FontSize(12);
                });
            });

            if (!string.IsNullOrWhiteSpace(_order.Notes))
            {
                col.Item().PaddingTop(20).Column(notes =>
                {
                    notes.Item().Text("Notes").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    notes.Item().PaddingTop(4).Text(_order.Notes);
                });
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter()
                .Text($"Internal document generated by Forge from sales order {_order.OrderNumber} — not a customer-issued purchase order.")
                .FontSize(8).FontColor(Colors.Grey.Medium);
            col.Item().AlignCenter().Text(text =>
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
