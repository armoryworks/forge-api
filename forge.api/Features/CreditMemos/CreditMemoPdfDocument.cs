using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.CreditMemos;

/// <summary>The customer-facing credit memo: lines, tax, total, what has been applied or refunded and what is left.</summary>
public class CreditMemoPdfDocument(CreditMemo memo, string? companyName, MoneyFormatter money) : IDocument
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
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
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
                    right.Item().Text("CREDIT MEMO").Bold().FontSize(24).FontColor(Colors.Blue.Darken2);
                    right.Item().Text(memo.CreditMemoNumber).FontSize(12);
                });
            });

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Credit To:").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    left.Item().Text(memo.Customer.CompanyName ?? memo.Customer.Name).SemiBold();
                    if (!string.IsNullOrEmpty(memo.Customer.Email))
                        left.Item().Text(memo.Customer.Email).FontSize(9);
                });
                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text($"Credit Date: {memo.CreditDate:MM/dd/yyyy}");
                    if (memo.Invoice is not null)
                        right.Item().Text($"Invoice #: {memo.Invoice.InvoiceNumber}");
                    right.Item().Text($"Reason: {memo.Reason}");
                    if (memo.Status == CreditMemoStatus.Voided)
                        right.Item().Text("VOIDED").Bold().FontColor(Colors.Red.Darken2);
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
                    cols.ConstantColumn(40);
                    cols.RelativeColumn(3);
                    cols.ConstantColumn(60);
                    cols.ConstantColumn(80);
                    cols.ConstantColumn(90);
                });

                table.Header(header =>
                {
                    var headerStyle = TextStyle.Default.SemiBold().FontSize(9).FontColor(Colors.White);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("#").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text("Description").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Qty").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Unit Price").Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text("Credit").Style(headerStyle);
                });

                var lines = memo.Lines.OrderBy(l => l.LineNumber).ToList();
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    table.Cell().Background(bg).Padding(5).Text(line.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    table.Cell().Background(bg).Padding(5).Text(line.Description);
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(MoneyFormat.Quantity(line.Quantity));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(money.Format(line.UnitPrice));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(money.Format(line.SignedTotal));
                }
            });

            col.Item().PaddingTop(10).AlignRight().Width(220).Column(totals =>
            {
                Row(totals, "Subtotal:", memo.Subtotal);
                if (memo.TaxAmount != 0m)
                    Row(totals, $"Tax ({MoneyFormat.Percent(memo.TaxRate)}):", memo.TaxAmount);
                totals.Item().PaddingTop(4).LineHorizontal(1);
                totals.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text("Total credit:").Bold().FontSize(12);
                    row.ConstantItem(100).AlignRight().Text(money.Format(memo.Total)).Bold().FontSize(12);
                });
                if (memo.AmountApplied > 0m)
                    Row(totals, "Applied to invoices:", -memo.AmountApplied);
                if (memo.AmountRefunded > 0m)
                    Row(totals, "Refunded:", -memo.AmountRefunded);
                if (memo.AmountApplied > 0m || memo.AmountRefunded > 0m)
                    Row(totals, "Credit remaining:", memo.OpenAmount);
            });

            if (!string.IsNullOrWhiteSpace(memo.Notes))
            {
                col.Item().PaddingTop(20).Column(notes =>
                {
                    notes.Item().Text("Notes").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    notes.Item().PaddingTop(4).Text(memo.Notes);
                });
            }
        });
    }

    private void Row(ColumnDescriptor totals, string label, decimal amount)
        => totals.Item().Row(row =>
        {
            row.RelativeItem().Text(label).SemiBold();
            row.ConstantItem(100).AlignRight().Text(money.Format(amount));
        });
}
