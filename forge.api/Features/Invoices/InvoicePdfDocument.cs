using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Core.Entities;

namespace Forge.Api.Features.Invoices;

public class InvoicePdfDocument : IDocument
{
    private readonly Invoice _invoice;
    private readonly string _companyName;

    public InvoicePdfDocument(Invoice invoice, string companyName)
    {
        _invoice = invoice;
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
                    right.Item().Text("INVOICE").Bold().FontSize(24).FontColor(Colors.Blue.Darken2);
                    right.Item().Text($"#{_invoice.InvoiceNumber}").FontSize(12);
                });
            });

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Bill To:").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    left.Item().Text(_invoice.Customer.CompanyName ?? _invoice.Customer.Name).SemiBold();
                    if (!string.IsNullOrEmpty(_invoice.Customer.Email))
                        left.Item().Text(_invoice.Customer.Email).FontSize(9);
                    if (!string.IsNullOrEmpty(_invoice.Customer.Phone))
                        left.Item().Text(_invoice.Customer.Phone).FontSize(9);
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text($"Invoice Date: {_invoice.InvoiceDate:MMM dd, yyyy}");
                    right.Item().Text($"Due Date: {_invoice.DueDate:MMM dd, yyyy}");
                    if (_invoice.CreditTerms.HasValue)
                        right.Item().Text($"Terms: {FormatCreditTerms(_invoice.CreditTerms.Value)}");
                    if (!string.IsNullOrEmpty(_invoice.SalesOrder?.OrderNumber))
                        right.Item().Text($"SO #: {_invoice.SalesOrder.OrderNumber}");
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

                var lines = _invoice.Lines.OrderBy(l => l.LineNumber).ToList();
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                    table.Cell().Background(bg).Padding(5).Text(line.LineNumber.ToString());
                    table.Cell().Background(bg).Padding(5).Text(line.Description);
                    table.Cell().Background(bg).Padding(5).Text(line.Part?.PartNumber ?? "—");
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.Quantity.ToString());
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.UnitPrice.ToString("C"));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(line.LineTotal.ToString("C"));
                }
            });

            col.Item().PaddingTop(10).AlignRight().Width(200).Column(totals =>
            {
                totals.Item().Row(row =>
                {
                    row.RelativeItem().Text("Subtotal:").SemiBold();
                    row.ConstantItem(100).AlignRight().Text(_invoice.Subtotal.ToString("C"));
                });

                if (_invoice.TaxRate > 0)
                {
                    totals.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Tax ({_invoice.TaxRate:P0}):").SemiBold();
                        row.ConstantItem(100).AlignRight().Text(_invoice.TaxAmount.ToString("C"));
                    });
                }

                totals.Item().PaddingTop(4).LineHorizontal(1);

                totals.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text("Total:").Bold().FontSize(12);
                    row.ConstantItem(100).AlignRight().Text(_invoice.Total.ToString("C")).Bold().FontSize(12);
                });

                if (_invoice.AmountPaid > 0)
                {
                    totals.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Paid:").FontColor(Colors.Green.Darken2);
                        row.ConstantItem(100).AlignRight().Text($"({_invoice.AmountPaid:C})").FontColor(Colors.Green.Darken2);
                    });

                    totals.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("Balance Due:").Bold().FontSize(12);
                        row.ConstantItem(100).AlignRight().Text(_invoice.BalanceDue.ToString("C")).Bold().FontSize(12);
                    });
                }
            });

            if (!string.IsNullOrWhiteSpace(_invoice.Notes))
            {
                col.Item().PaddingTop(20).Column(notes =>
                {
                    notes.Item().Text("Notes").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    notes.Item().PaddingTop(4).Text(_invoice.Notes);
                });
            }
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

    private static string FormatCreditTerms(CreditTerms terms) => terms switch
    {
        CreditTerms.DueOnReceipt => "Due on Receipt",
        CreditTerms.Net15 => "Net 15",
        CreditTerms.Net30 => "Net 30",
        CreditTerms.Net45 => "Net 45",
        CreditTerms.Net60 => "Net 60",
        CreditTerms.Net90 => "Net 90",
        _ => terms.ToString()
    };
}
