using System.Globalization;

using Forge.Api.Services;
using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Customers;

public static class CustomerStatementPdfDocument
{
    private const string Styles = """
        .statement-head { display: flex; justify-content: space-between; align-items: flex-start; }
        .statement-head .company { font-size: 18pt; font-weight: 700; padding-right: 20pt; }
        .statement-head .right { width: 200pt; flex: none; }
        .statement-head .title { font-size: 14pt; font-weight: 700; }
        .bill-to { padding-top: 10pt; }
        .bill-to .caption { font-size: 9pt; font-weight: 700; }
        .bill-to .name { font-weight: 700; }
        .statement-rule { border: 0; border-top: 1pt solid #000; margin: 10pt 0 0; }
        .statement-body { padding-top: 10pt; }
        .section-title { font-size: 12pt; font-weight: 700; }
        .section-title.later { padding-top: 15pt; }
        table.statement { width: 100%; border-collapse: collapse; margin-top: 5pt; font-size: 9pt; }
        table.statement thead { display: table-header-group; }
        table.statement thead tr { background: #f0f0f0; }
        table.statement th { font-weight: 700; padding: 4pt; text-align: left; }
        table.statement td { padding: 4pt; vertical-align: top; }
        table.statement tr { break-inside: avoid; }
        table.statement .num { text-align: right; white-space: nowrap; }
        .balance { display: flex; justify-content: flex-end; align-items: baseline; padding-top: 4pt; font-weight: 700; break-inside: avoid; }
        .balance.first { padding-top: 20pt; }
        .balance .caption { width: 140pt; font-size: 12pt; }
        .balance .amount { width: 130pt; font-size: 14pt; text-align: right; white-space: nowrap; }
        """;

    public static HtmlPdfDocument Build(
        Customer customer,
        List<Invoice> invoices,
        List<Payment> payments,
        string? companyName,
        DateTimeOffset statementDate,
        IReadOnlyDictionary<int, MoneyFormatter>? currencyMoney = null,
        IReadOnlyDictionary<int, MoneyFormatter>? paymentMoney = null,
        MoneyFormatter? baseMoney = null,
        List<CreditMemo>? creditMemos = null,
        List<CustomerRefund>? refunds = null)
    {
        var fallback = baseMoney ?? MoneyFormat.For(null, "USD");
        var memos = creditMemos ?? [];
        var refundList = refunds ?? [];

        MoneyFormatter CurrencyMoney(int currencyId)
            => currencyMoney is not null && currencyMoney.TryGetValue(currencyId, out var money) ? money : fallback;

        MoneyFormatter PaymentMoney(Payment payment)
            => paymentMoney is not null && paymentMoney.TryGetValue(payment.Id, out var money) ? money : fallback;

        var openCredits = memos.Where(m => m.OpenAmount > 0m).ToList();
        var totalsDue = invoices.Select(i => (Money: CurrencyMoney(i.CurrencyId), Amount: i.BalanceDue))
            .Concat(openCredits.Select(m => (Money: CurrencyMoney(m.CurrencyId), Amount: -m.OpenAmount)))
            .GroupBy(x => x.Money)
            .Select(g => g.Key.Format(g.Sum(x => x.Amount)))
            .DefaultIfEmpty(fallback.Format(0m))
            .Select((totalDue, i) => Html.Of(
                $"<div class=\"balance{(i == 0 ? " first" : string.Empty)}\"><div class=\"caption\">Total Balance Due:</div><div class=\"amount\">{totalDue}</div></div>"))
            .ToList();

        var body = Html.Of($"""
            {Header(customer, companyName, statementDate)}
            <div class="statement-body">
              {Invoices(invoices, CurrencyMoney)}
              {Payments(payments, PaymentMoney)}
              {CreditMemos(memos, CurrencyMoney)}
              {Refunds(refundList, CurrencyMoney)}
              {totalsDue}
            </div>
            """);

        var footerLabel = $"Statement generated {statementDate.ToString("MMMM dd, yyyy", CultureInfo.InvariantCulture)}";
        return PdfHtml.Document($"Customer Statement {customer.Name}", body, footerLabel, styles: Styles);
    }

    private static Html Header(Customer customer, string? companyName, DateTimeOffset statementDate)
        => Html.Of($"""
            <div class="statement-head">
              <div class="company">{companyName}</div>
              <div class="right">
                <div class="title">CUSTOMER STATEMENT</div>
                <div class="small">Date: {statementDate:MMM dd, yyyy}</div>
              </div>
            </div>
            <div class="bill-to">
              <div class="caption">Bill To:</div>
              <div class="name">{customer.Name}</div>
              {(string.IsNullOrWhiteSpace(customer.Email) ? Html.Empty : Html.Of($"<div class=\"small\">{customer.Email}</div>"))}
              {(string.IsNullOrWhiteSpace(customer.Phone) ? Html.Empty : Html.Of($"<div class=\"small\">{customer.Phone}</div>"))}
            </div>
            <hr class="statement-rule">
            """);

    private static Html Invoices(List<Invoice> invoices, Func<int, MoneyFormatter> currencyMoney)
    {
        var rows = invoices.OrderByDescending(i => i.InvoiceDate).Select(inv =>
        {
            var money = currencyMoney(inv.CurrencyId);
            return Html.Of($"""
                <tr>
                  <td>{inv.InvoiceNumber}</td>
                  <td>{inv.InvoiceDate:MM/dd/yyyy}</td>
                  <td>{inv.DueDate:MM/dd/yyyy}</td>
                  <td>{inv.Status}</td>
                  <td class="num">{money.Format(inv.Total)}</td>
                  <td class="num">{money.Format(inv.AmountPaid)}</td>
                  <td class="num">{money.Format(inv.DiscountsTaken + inv.CreditsApplied)}</td>
                  <td class="num">{money.Format(inv.BalanceDue)}</td>
                </tr>
                """);
        });

        return Html.Of($"""
            <div class="section-title">Invoices</div>
            <table class="statement">
              <colgroup><col style="width:60pt"><col style="width:60pt"><col style="width:60pt"><col><col style="width:62pt"><col style="width:62pt"><col style="width:62pt"><col style="width:62pt"></colgroup>
              <thead><tr><th>Invoice #</th><th>Date</th><th>Due Date</th><th>Status</th><th class="num">Total</th><th class="num">Paid</th><th class="num">Discounts / credits</th><th class="num">Balance</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html Payments(List<Payment> payments, Func<Payment, MoneyFormatter> paymentMoney)
    {
        if (payments.Count == 0)
            return Html.Empty;

        var rows = payments.OrderByDescending(p => p.PaymentDate).Select(pmt => Html.Of($"""
            <tr>
              <td>{pmt.PaymentNumber}</td>
              <td>{pmt.PaymentDate:MM/dd/yyyy}</td>
              <td>{pmt.Method}</td>
              <td class="num">{paymentMoney(pmt).Format(pmt.Amount)}</td>
            </tr>
            """));

        return Html.Of($"""
            <div class="section-title later">Payment History</div>
            <table class="statement">
              <colgroup><col style="width:100pt"><col style="width:90pt"><col><col style="width:100pt"></colgroup>
              <thead><tr><th>Payment #</th><th>Date</th><th>Method</th><th class="num">Amount</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html CreditMemos(List<CreditMemo> memos, Func<int, MoneyFormatter> currencyMoney)
    {
        if (memos.Count == 0)
            return Html.Empty;

        var rows = memos.OrderByDescending(m => m.CreditDate).Select(memo =>
        {
            var money = currencyMoney(memo.CurrencyId);
            return Html.Of($"""
                <tr>
                  <td>{memo.CreditMemoNumber}</td>
                  <td>{memo.CreditDate:MM/dd/yyyy}</td>
                  <td>{memo.Reason}</td>
                  <td class="num">{money.Format(memo.Total)}</td>
                  <td class="num">{money.Format(memo.OpenAmount)}</td>
                </tr>
                """);
        });

        return Html.Of($"""
            <div class="section-title later">Credit Memos</div>
            <table class="statement">
              <colgroup><col style="width:80pt"><col style="width:70pt"><col><col style="width:80pt"><col style="width:80pt"></colgroup>
              <thead><tr><th>Credit #</th><th>Date</th><th>Reason</th><th class="num">Credit</th><th class="num">Unused</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html Refunds(List<CustomerRefund> refunds, Func<int, MoneyFormatter> currencyMoney)
    {
        if (refunds.Count == 0)
            return Html.Empty;

        var rows = refunds.OrderByDescending(r => r.RefundDate).Select(refund => Html.Of($"""
            <tr>
              <td>{refund.RefundNumber}</td>
              <td>{refund.RefundDate:MM/dd/yyyy}</td>
              <td>{refund.Method}</td>
              <td class="num">{currencyMoney(refund.CurrencyId).Format(refund.Amount)}</td>
            </tr>
            """));

        return Html.Of($"""
            <div class="section-title later">Refunds</div>
            <table class="statement">
              <colgroup><col style="width:100pt"><col style="width:90pt"><col><col style="width:100pt"></colgroup>
              <thead><tr><th>Refund #</th><th>Date</th><th>Method</th><th class="num">Amount</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }
}
