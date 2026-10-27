using System.Globalization;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.CreditMemos;

/// <summary>The customer-facing credit memo: lines, tax, total, what has been applied or refunded and what is left.</summary>
public class CreditMemoPdfDocument(
    CreditMemo memo, string? companyName, MoneyFormatter money, CreditMemoPdfLabels? labels = null) : IDocument
{
    private readonly CreditMemoPdfLabels _labels = labels ?? CreditMemoPdfLabels.English;

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
                text.Span(_labels.Page);
                text.CurrentPageNumber();
                text.Span(_labels.Of);
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
                    right.Item().Text(_labels.Title).Bold().FontSize(24).FontColor(Colors.Blue.Darken2);
                    right.Item().Text(memo.CreditMemoNumber).FontSize(12);
                });
            });

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(_labels.CreditTo).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    left.Item().Text(memo.Customer.CompanyName ?? memo.Customer.Name).SemiBold();
                    if (!string.IsNullOrEmpty(memo.Customer.Email))
                        left.Item().Text(memo.Customer.Email).FontSize(9);
                });
                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text($"{_labels.CreditDate} {memo.CreditDate.ToString(_labels.DateFormat, CultureInfo.InvariantCulture)}");
                    if (memo.Invoice is not null)
                        right.Item().Text($"{_labels.InvoiceNumber} {memo.Invoice.InvoiceNumber}");
                    right.Item().Text($"{_labels.Reason} {memo.Reason}");
                    if (memo.Status == CreditMemoStatus.Voided)
                        right.Item().Text(_labels.Voided).Bold().FontColor(Colors.Red.Darken2);
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
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text(_labels.Description).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text(_labels.Quantity).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text(_labels.UnitPrice).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text(_labels.Credit).Style(headerStyle);
                });

                var lines = memo.Lines.OrderBy(l => l.LineNumber).ToList();
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    table.Cell().Background(bg).Padding(5).Text(line.LineNumber.ToString(CultureInfo.InvariantCulture));
                    table.Cell().Background(bg).Padding(5).Text(line.Description);
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(MoneyFormat.Quantity(line.Quantity));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(money.Format(line.UnitPrice));
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(money.Format(line.SignedTotal));
                }
            });

            col.Item().PaddingTop(10).AlignRight().Width(220).Column(totals =>
            {
                Row(totals, _labels.Subtotal, memo.Subtotal);
                if (memo.TaxAmount != 0m)
                    Row(totals, string.Format(CultureInfo.InvariantCulture, _labels.Tax, MoneyFormat.Percent(memo.TaxRate)), memo.TaxAmount);
                totals.Item().PaddingTop(4).LineHorizontal(1);
                totals.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text(_labels.TotalCredit).Bold().FontSize(12);
                    row.ConstantItem(100).AlignRight().Text(money.Format(memo.Total)).Bold().FontSize(12);
                });
                if (memo.AmountApplied > 0m)
                    Row(totals, _labels.AppliedToInvoices, -memo.AmountApplied);
                if (memo.AmountRefunded > 0m)
                    Row(totals, _labels.Refunded, -memo.AmountRefunded);
                if (memo.AmountApplied > 0m || memo.AmountRefunded > 0m)
                    Row(totals, _labels.CreditRemaining, memo.OpenAmount);
            });

            if (!string.IsNullOrWhiteSpace(memo.Notes))
            {
                col.Item().PaddingTop(20).Column(notes =>
                {
                    notes.Item().Text(_labels.Notes).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
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
