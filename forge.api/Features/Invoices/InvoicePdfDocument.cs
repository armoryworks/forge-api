using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Api.Services;
using Forge.Core.Entities;

namespace Forge.Api.Features.Invoices;

public class InvoicePdfDocument : IDocument
{
    private readonly Invoice _invoice;
    private readonly string? _companyName;
    private readonly MoneyFormatter _money;
    private readonly InvoiceDiscountOffer _offer;

    public InvoicePdfDocument(Invoice invoice, string? companyName, MoneyFormatter? money = null, InvoiceDiscountOffer? offer = null)
    {
        _invoice = invoice;
        _companyName = companyName;
        _money = money ?? MoneyFormat.For(null, "USD");
        _offer = offer ?? InvoiceDiscountOffer.None;
    }

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
                    if (_companyName is not null)
                        left.Item().Text(_companyName).Bold().FontSize(18);
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text("INVOICE").Bold().FontSize(24).FontColor(Colors.Blue.Darken2);
                    right.Item().Text(_invoice.InvoiceNumber).FontSize(12);
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
                    if (_offer.TermsName is { Length: > 0 } termsName)
                        right.Item().Text($"Terms: {termsName}");
                    else if (_invoice.CreditTerms.HasValue)
                        right.Item().Text($"Terms: {FormatCreditTerms(_invoice.CreditTerms.Value)}");
                    if (_offer.HasDiscount)
                        right.Item().Text($"Pay {_money.Format(_offer.PayAmount)} by {_offer.Deadline:MM/dd/yyyy} to take the discount.").SemiBold();
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

                var lines = _invoice.Lines.OrderBy(l => l.IsFreight).ThenBy(l => l.LineNumber).ToList();
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                    table.Cell().Background(bg).Padding(5).Text(line.LineNumber.ToString());
                    table.Cell().Background(bg).Padding(5).Text(line.Description);
                    table.Cell().Background(bg).Padding(5).Text(line.Part?.PartNumber ?? "—");
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(MoneyFormat.Quantity(line.Quantity));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(_money.Format(line.UnitPrice));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(_money.Format(line.LineTotal));
                }
            });

            col.Item().PaddingTop(10).AlignRight().Width(200).Column(totals =>
            {
                totals.Item().Row(row =>
                {
                    row.RelativeItem().Text("Subtotal:").SemiBold();
                    row.ConstantItem(100).AlignRight().Text(_money.Format(_invoice.Subtotal - _invoice.FreightSubtotal));
                });

                if (_invoice.Lines.Any(l => l.IsFreight))
                {
                    totals.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Freight:").SemiBold();
                        row.ConstantItem(100).AlignRight().Text(_money.Format(_invoice.FreightSubtotal));
                    });
                }

                if (_invoice.TaxRate > 0)
                {
                    totals.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Tax ({MoneyFormat.Percent(_invoice.TaxRate)}):").SemiBold();
                        row.ConstantItem(100).AlignRight().Text(_money.Format(_invoice.TaxAmount));
                    });
                }

                totals.Item().PaddingTop(4).LineHorizontal(1);

                totals.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text("Total:").Bold().FontSize(12);
                    row.ConstantItem(100).AlignRight().Text(_money.Format(_invoice.Total)).Bold().FontSize(12);
                });

                if (_invoice.AmountPaid > 0)
                {
                    totals.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Paid:").FontColor(Colors.Green.Darken2);
                        row.ConstantItem(100).AlignRight().Text($"({_money.Format(_invoice.AmountPaid)})").FontColor(Colors.Green.Darken2);
                    });

                    if (_invoice.DiscountsTaken > 0)
                    {
                        totals.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Early-payment discount:").FontColor(Colors.Green.Darken2);
                            row.ConstantItem(100).AlignRight().Text($"({_money.Format(_invoice.DiscountsTaken)})").FontColor(Colors.Green.Darken2);
                        });
                    }

                    totals.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("Balance Due:").Bold().FontSize(12);
                        row.ConstantItem(100).AlignRight().Text(_money.Format(_invoice.BalanceDue)).Bold().FontSize(12);
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

    private static string FormatCreditTerms(Core.Enums.CreditTerms terms) => terms switch
    {
        Core.Enums.CreditTerms.DueOnReceipt => "Due on Receipt",
        Core.Enums.CreditTerms.Net15 => "Net 15",
        Core.Enums.CreditTerms.Net30 => "Net 30",
        Core.Enums.CreditTerms.Net45 => "Net 45",
        Core.Enums.CreditTerms.Net60 => "Net 60",
        Core.Enums.CreditTerms.Net90 => "Net 90",
        _ => terms.ToString()
    };
}
