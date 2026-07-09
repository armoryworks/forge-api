using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// A compact "Sales Order Acceptance" PDF sent to the customer for e-signature. Purpose-built (not
/// coupled to the customer-PO doc) so it's always renderable from the SO itself — header, line summary,
/// total, and a customer acceptance/signature block.
/// </summary>
public class SalesOrderAcceptancePdfDocument(
    string companyName, string orderNumber, string customerName, string? termsText,
    IReadOnlyList<SalesOrderAcceptancePdfDocument.Line> lines) : IDocument
{
    public record Line(string Description, decimal Quantity, decimal UnitPrice);

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginHorizontal(50);
            page.MarginVertical(40);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text(companyName).FontSize(16).SemiBold();
                col.Item().Text("Sales Order Acceptance").FontSize(13).FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(4).Text($"Order {orderNumber}  ·  {customerName}");
            });

            page.Content().PaddingVertical(16).Column(col =>
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(5); c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(2); });
                    table.Header(h =>
                    {
                        h.Cell().Text("Description").SemiBold();
                        h.Cell().AlignRight().Text("Qty").SemiBold();
                        h.Cell().AlignRight().Text("Unit").SemiBold();
                        h.Cell().AlignRight().Text("Amount").SemiBold();
                    });
                    decimal total = 0m;
                    foreach (var l in lines)
                    {
                        var amount = l.Quantity * l.UnitPrice;
                        total += amount;
                        table.Cell().Text(l.Description);
                        table.Cell().AlignRight().Text(l.Quantity.ToString("0.####"));
                        table.Cell().AlignRight().Text(l.UnitPrice.ToString("C"));
                        table.Cell().AlignRight().Text(amount.ToString("C"));
                    }
                    table.Cell().ColumnSpan(3).AlignRight().PaddingTop(6).Text("Total").SemiBold();
                    table.Cell().AlignRight().PaddingTop(6).Text(total.ToString("C")).SemiBold();
                });

                if (!string.IsNullOrWhiteSpace(termsText))
                    col.Item().PaddingTop(16).Text(termsText).FontSize(8).FontColor(Colors.Grey.Darken1);

                col.Item().PaddingTop(28).Text(
                    "By signing below, the customer accepts the terms of this Sales Order and authorizes production.")
                    .FontSize(9);
                col.Item().PaddingTop(28).Row(row =>
                {
                    row.RelativeItem().Column(c => { c.Item().LineHorizontal(1); c.Item().Text("Customer signature").FontSize(8); });
                    row.ConstantItem(30);
                    row.RelativeItem().Column(c => { c.Item().LineHorizontal(1); c.Item().Text("Date").FontSize(8); });
                });
            });

            page.Footer().AlignCenter().Text(t => t.Span($"{companyName} · Order {orderNumber}").FontSize(8).FontColor(Colors.Grey.Medium));
        });
    }
}
