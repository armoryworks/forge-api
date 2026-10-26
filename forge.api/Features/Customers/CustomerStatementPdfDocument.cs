using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Api.Services;
using Forge.Core.Entities;

namespace Forge.Api.Features.Customers;

public class CustomerStatementPdfDocument(
    Customer customer,
    List<Invoice> invoices,
    List<Payment> payments,
    string? companyName,
    DateTimeOffset statementDate,
    IReadOnlyDictionary<int, MoneyFormatter>? currencyMoney = null,
    IReadOnlyDictionary<int, MoneyFormatter>? paymentMoney = null,
    MoneyFormatter? baseMoney = null,
    List<CreditMemo>? creditMemos = null,
    List<CustomerRefund>? refunds = null) : IDocument
{
    private readonly MoneyFormatter _baseMoney = baseMoney ?? MoneyFormat.For(null, "USD");

    private readonly List<CreditMemo> _creditMemos = creditMemos ?? [];

    private readonly List<CustomerRefund> _refunds = refunds ?? [];

    private MoneyFormatter CurrencyMoney(int currencyId)
        => currencyMoney is not null && currencyMoney.TryGetValue(currencyId, out var money) ? money : _baseMoney;

    private MoneyFormatter InvoiceMoney(Invoice invoice)
        => currencyMoney is not null && currencyMoney.TryGetValue(invoice.CurrencyId, out var money) ? money : _baseMoney;

    private MoneyFormatter PaymentMoney(Payment payment)
        => paymentMoney is not null && paymentMoney.TryGetValue(payment.Id, out var money) ? money : _baseMoney;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginHorizontal(50);
            page.MarginVertical(40);
            page.DefaultTextStyle(x => x.WithScriptFallbacks().FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    var companyCell = row.RelativeItem();
                    if (companyName is not null)
                        companyCell.Text(companyName).FontSize(18).Bold();
                    row.ConstantItem(200).AlignRight().Column(right =>
                    {
                        right.Item().Text("CUSTOMER STATEMENT").FontSize(14).Bold();
                        right.Item().Text($"Date: {statementDate:MMM dd, yyyy}").FontSize(9);
                    });
                });

                col.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text("Bill To:").Bold().FontSize(9);
                        left.Item().Text(customer.Name).Bold();
                        if (!string.IsNullOrWhiteSpace(customer.Email))
                            left.Item().Text(customer.Email).FontSize(9);
                        if (!string.IsNullOrWhiteSpace(customer.Phone))
                            left.Item().Text(customer.Phone).FontSize(9);
                    });
                });

                col.Item().PaddingTop(10).LineHorizontal(1);
            });

            page.Content().PaddingTop(10).Column(content =>
            {
                content.Item().Text("Invoices").FontSize(12).Bold();
                content.Item().PaddingTop(5).Table(table =>
                {
                    // WU-15 / RPT-CUSTSTMT-001: column widths previously summed
                    // to 520pt fixed plus a relative column on a Letter page
                    // with 50pt horizontal margins (~512pt content area).
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(60);
                        cols.ConstantColumn(60);
                        cols.ConstantColumn(60);
                        cols.RelativeColumn();
                        cols.ConstantColumn(62);
                        cols.ConstantColumn(62);
                        cols.ConstantColumn(62);
                        cols.ConstantColumn(62);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background("#f0f0f0").Padding(4).Text("Invoice #").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).Text("Date").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).Text("Due Date").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).Text("Status").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Total").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Paid").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Discounts / credits").Bold().FontSize(9);
                        header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Balance").Bold().FontSize(9);
                    });

                    foreach (var inv in invoices.OrderByDescending(i => i.InvoiceDate))
                    {
                        table.Cell().Padding(4).Text(inv.InvoiceNumber).FontSize(9);
                        table.Cell().Padding(4).Text(inv.InvoiceDate.ToString("MM/dd/yyyy")).FontSize(9);
                        table.Cell().Padding(4).Text(inv.DueDate.ToString("MM/dd/yyyy")).FontSize(9);
                        table.Cell().Padding(4).Text(inv.Status.ToString()).FontSize(9);
                        var money = InvoiceMoney(inv);
                        table.Cell().Padding(4).AlignRight().Text(money.Format(inv.Total)).FontSize(9);
                        table.Cell().Padding(4).AlignRight().Text(money.Format(inv.AmountPaid)).FontSize(9);
                        table.Cell().Padding(4).AlignRight().Text(money.Format(inv.DiscountsTaken + inv.CreditsApplied)).FontSize(9);
                        table.Cell().Padding(4).AlignRight().Text(money.Format(inv.BalanceDue)).FontSize(9);
                    }
                });

                if (payments.Count > 0)
                {
                    content.Item().PaddingTop(15).Text("Payment History").FontSize(12).Bold();
                    content.Item().PaddingTop(5).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(100);
                            cols.ConstantColumn(90);
                            cols.RelativeColumn();
                            cols.ConstantColumn(100);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Payment #").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Date").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Method").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Amount").Bold().FontSize(9);
                        });

                        foreach (var pmt in payments.OrderByDescending(p => p.PaymentDate))
                        {
                            table.Cell().Padding(4).Text(pmt.PaymentNumber).FontSize(9);
                            table.Cell().Padding(4).Text(pmt.PaymentDate.ToString("MM/dd/yyyy")).FontSize(9);
                            table.Cell().Padding(4).Text(pmt.Method.ToString()).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(PaymentMoney(pmt).Format(pmt.Amount)).FontSize(9);
                        }
                    });
                }

                var openCredits = _creditMemos.Where(m => m.OpenAmount > 0m).ToList();
                if (_creditMemos.Count > 0)
                {
                    content.Item().PaddingTop(15).Text("Credit Memos").FontSize(12).Bold();
                    content.Item().PaddingTop(5).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(80);
                            cols.ConstantColumn(70);
                            cols.RelativeColumn();
                            cols.ConstantColumn(80);
                            cols.ConstantColumn(80);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Credit #").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Date").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Reason").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Credit").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Unused").Bold().FontSize(9);
                        });

                        foreach (var memo in _creditMemos.OrderByDescending(m => m.CreditDate))
                        {
                            var money = CurrencyMoney(memo.CurrencyId);
                            table.Cell().Padding(4).Text(memo.CreditMemoNumber).FontSize(9);
                            table.Cell().Padding(4).Text(memo.CreditDate.ToString("MM/dd/yyyy")).FontSize(9);
                            table.Cell().Padding(4).Text(memo.Reason).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(money.Format(memo.Total)).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(money.Format(memo.OpenAmount)).FontSize(9);
                        }
                    });
                }

                if (_refunds.Count > 0)
                {
                    content.Item().PaddingTop(15).Text("Refunds").FontSize(12).Bold();
                    content.Item().PaddingTop(5).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(100);
                            cols.ConstantColumn(90);
                            cols.RelativeColumn();
                            cols.ConstantColumn(100);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Refund #").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Date").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).Text("Method").Bold().FontSize(9);
                            header.Cell().Background("#f0f0f0").Padding(4).AlignRight().Text("Amount").Bold().FontSize(9);
                        });

                        foreach (var refund in _refunds.OrderByDescending(r => r.RefundDate))
                        {
                            table.Cell().Padding(4).Text(refund.RefundNumber).FontSize(9);
                            table.Cell().Padding(4).Text(refund.RefundDate.ToString("MM/dd/yyyy")).FontSize(9);
                            table.Cell().Padding(4).Text(refund.Method.ToString()).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(CurrencyMoney(refund.CurrencyId).Format(refund.Amount)).FontSize(9);
                        }
                    });
                }

                var totalsDue = invoices.Select(i => (Money: InvoiceMoney(i), Amount: i.BalanceDue))
                    .Concat(openCredits.Select(m => (Money: CurrencyMoney(m.CurrencyId), Amount: -m.OpenAmount)))
                    .GroupBy(x => x.Money)
                    .Select(g => g.Key.Format(g.Sum(x => x.Amount)))
                    .DefaultIfEmpty(_baseMoney.Format(0m))
                    .ToList();
                for (var i = 0; i < totalsDue.Count; i++)
                {
                    var totalDue = totalsDue[i];
                    content.Item().PaddingTop(i == 0 ? 20 : 4).AlignRight().Row(row =>
                    {
                        row.ConstantItem(140).Text("Total Balance Due:").Bold().FontSize(12);
                        row.ConstantItem(130).AlignRight().Text(totalDue).Bold().FontSize(14);
                    });
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span($"Statement generated {statementDate:MMMM dd, yyyy}").FontSize(8);
                text.Span(" | Page ").FontSize(8);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" of ").FontSize(8);
                text.TotalPages().FontSize(8);
            });
        });
    }
}
