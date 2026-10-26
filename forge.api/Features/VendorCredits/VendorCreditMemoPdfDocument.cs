using System.Globalization;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.VendorCredits;

/// <summary>The vendor credit or debit memo document: lines, tax, total, what has been applied to bills and what is left.</summary>
public class VendorCreditMemoPdfDocument(
    VendorCreditMemo memo, string? companyName, MoneyFormatter money, VendorDocumentPdfLabels labels) : IDocument
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
                    right.Item().Text(memo.Kind == VendorCreditMemoKind.DebitMemo ? labels["debitMemo"] : labels["creditMemo"])
                        .Bold().FontSize(20).FontColor(Colors.Blue.Darken2);
                    right.Item().Text(memo.MemoNumber).FontSize(12);
                });
            });

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(labels["vendor"]).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    left.Item().Text(memo.Vendor.CompanyName).SemiBold();
                    if (!string.IsNullOrEmpty(memo.Vendor.Email))
                        left.Item().Text(memo.Vendor.Email).FontSize(9);
                });
                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text($"{labels["memoDate"]} {memo.MemoDate.ToString(labels.DateFormat, CultureInfo.InvariantCulture)}");
                    if (memo.VendorBill is not null)
                        right.Item().Text($"{labels["bill"]} {memo.VendorBill.BillNumber}");
                    if (memo.VendorReturn is not null)
                        right.Item().Text($"{labels["return"]} {memo.VendorReturn.ReturnNumber}");
                    if (!string.IsNullOrWhiteSpace(memo.VendorReference))
                        right.Item().Text($"{labels["vendorReference"]} {memo.VendorReference}");
                    right.Item().Text($"{labels["reason"]} {memo.Reason}");
                    if (memo.Status == VendorCreditMemoStatus.Voided)
                        right.Item().Text(labels["voided"]).Bold().FontColor(Colors.Red.Darken2);
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
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text(labels["number"]).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text(labels["description"]).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text(labels["qty"]).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text(labels["unitPrice"]).Style(headerStyle);
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5).AlignRight().Text(labels["amount"]).Style(headerStyle);
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
                    table.Cell().Background(bg).Padding(5).AlignRight().Text(money.Format(line.LineTotal));
                }
            });

            col.Item().PaddingTop(10).AlignRight().Width(220).Column(totals =>
            {
                Row(totals, labels["subtotal"], memo.Subtotal);
                if (memo.TaxAmount != 0m)
                    Row(totals, labels["tax"], memo.TaxAmount);
                totals.Item().PaddingTop(4).LineHorizontal(1);
                totals.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text(labels["totalCredit"]).Bold().FontSize(12);
                    row.ConstantItem(100).AlignRight().Text(money.Format(memo.Total)).Bold().FontSize(12);
                });
                if (memo.AmountApplied > 0m)
                {
                    Row(totals, labels["applied"], -memo.AmountApplied);
                    Row(totals, labels["remaining"], memo.OpenAmount);
                }
            });

            if (!string.IsNullOrWhiteSpace(memo.Notes))
            {
                col.Item().PaddingTop(20).Column(notes =>
                {
                    notes.Item().Text(labels["notes"]).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
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
