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

    private sealed record Section(string Title, Html Columns, Html Headings, IReadOnlyList<Html> Rows, int ColumnCount);

    private sealed record Slice(Section Section, int Start, int Count);

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
        => BuildParts(
            customer, invoices, payments, companyName, statementDate, currencyMoney, paymentMoney, baseMoney,
            creditMemos, refunds, int.MaxValue)[0];

    public static IReadOnlyList<HtmlPdfDocument> BuildParts(
        Customer customer,
        List<Invoice> invoices,
        List<Payment> payments,
        string? companyName,
        DateTimeOffset statementDate,
        IReadOnlyDictionary<int, MoneyFormatter>? currencyMoney = null,
        IReadOnlyDictionary<int, MoneyFormatter>? paymentMoney = null,
        MoneyFormatter? baseMoney = null,
        List<CreditMemo>? creditMemos = null,
        List<CustomerRefund>? refunds = null,
        int cellsPerPart = PdfParts.CellsPerPart / 2)
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

        var sections = new List<Section> { Invoices(invoices, CurrencyMoney) };
        if (payments.Count > 0)
            sections.Add(Payments(payments, PaymentMoney));
        if (memos.Count > 0)
            sections.Add(CreditMemos(memos, CurrencyMoney));
        if (refundList.Count > 0)
            sections.Add(Refunds(refundList, CurrencyMoney));

        var parts = Partition(sections, cellsPerPart);
        var footerLabel = $"Statement generated {statementDate.ToString("MMMM dd, yyyy", CultureInfo.InvariantCulture)}";

        return parts.Select((slices, i) =>
        {
            var body = Html.Of($"""
                {Header(customer, companyName, statementDate, i + 1, parts.Count)}
                <div class="statement-body">
                  {slices.Select((slice, n) => Table(slice, n > 0))}
                  {(i == parts.Count - 1 ? Html.Join(totalsDue) : Html.Empty)}
                </div>
                """);
            return PdfHtml.Document(
                $"Customer Statement {customer.Name}", body, PdfParts.Label(footerLabel, i + 1, parts.Count), styles: Styles);
        }).ToList();
    }

    private static List<List<Slice>> Partition(IReadOnlyList<Section> sections, int cellsPerPart)
    {
        var parts = new List<List<Slice>> { new() };
        var room = cellsPerPart;
        foreach (var section in sections)
        {
            var start = 0;
            do
            {
                if (room < section.ColumnCount && start < section.Rows.Count)
                {
                    parts.Add([]);
                    room = Math.Max(cellsPerPart, section.ColumnCount);
                }

                var count = Math.Min(room / section.ColumnCount, section.Rows.Count - start);
                parts[^1].Add(new Slice(section, start, count));
                room -= count * section.ColumnCount;
                start += count;
            }
            while (start < section.Rows.Count);
        }

        return parts;
    }

    private static Html Header(Customer customer, string? companyName, DateTimeOffset statementDate, int part, int parts)
        => Html.Of($"""
            <div class="statement-head">
              <div class="company">{companyName}</div>
              <div class="right">
                <div class="title">CUSTOMER STATEMENT</div>
                <div class="small">Date: {statementDate:MMM dd, yyyy}</div>
                {(parts == 1 ? Html.Empty : Html.Of($"<div class=\"small\">Part {part} of {parts}</div>"))}
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

    private static Html Table(Slice slice, bool later)
    {
        var section = slice.Section;
        var title = slice.Start == 0 ? section.Title : $"{section.Title} (continued)";
        return Html.Of($"""
            <div class="section-title{(later ? " later" : string.Empty)}">{title}</div>
            <table class="statement">
              <colgroup>{section.Columns}</colgroup>
              <thead><tr>{section.Headings}</tr></thead>
              <tbody>{section.Rows.Skip(slice.Start).Take(slice.Count)}</tbody>
            </table>
            """);
    }

    private static Section Invoices(List<Invoice> invoices, Func<int, MoneyFormatter> currencyMoney)
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
        }).ToList();

        return new Section(
            "Invoices",
            Html.Of($"<col style=\"width:60pt\"><col style=\"width:60pt\"><col style=\"width:60pt\"><col><col style=\"width:62pt\"><col style=\"width:62pt\"><col style=\"width:62pt\"><col style=\"width:62pt\">"),
            Html.Of($"<th>Invoice #</th><th>Date</th><th>Due Date</th><th>Status</th><th class=\"num\">Total</th><th class=\"num\">Paid</th><th class=\"num\">Discounts / credits</th><th class=\"num\">Balance</th>"),
            rows,
            8);
    }

    private static Section Payments(List<Payment> payments, Func<Payment, MoneyFormatter> paymentMoney)
    {
        var rows = payments.OrderByDescending(p => p.PaymentDate).Select(pmt => Html.Of($"""
            <tr>
              <td>{pmt.PaymentNumber}</td>
              <td>{pmt.PaymentDate:MM/dd/yyyy}</td>
              <td>{pmt.Method}</td>
              <td class="num">{paymentMoney(pmt).Format(pmt.Amount)}</td>
            </tr>
            """)).ToList();

        return new Section(
            "Payment History",
            Html.Of($"<col style=\"width:100pt\"><col style=\"width:90pt\"><col><col style=\"width:100pt\">"),
            Html.Of($"<th>Payment #</th><th>Date</th><th>Method</th><th class=\"num\">Amount</th>"),
            rows,
            4);
    }

    private static Section CreditMemos(List<CreditMemo> memos, Func<int, MoneyFormatter> currencyMoney)
    {
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
        }).ToList();

        return new Section(
            "Credit Memos",
            Html.Of($"<col style=\"width:80pt\"><col style=\"width:70pt\"><col><col style=\"width:80pt\"><col style=\"width:80pt\">"),
            Html.Of($"<th>Credit #</th><th>Date</th><th>Reason</th><th class=\"num\">Credit</th><th class=\"num\">Unused</th>"),
            rows,
            5);
    }

    private static Section Refunds(List<CustomerRefund> refunds, Func<int, MoneyFormatter> currencyMoney)
    {
        var rows = refunds.OrderByDescending(r => r.RefundDate).Select(refund => Html.Of($"""
            <tr>
              <td>{refund.RefundNumber}</td>
              <td>{refund.RefundDate:MM/dd/yyyy}</td>
              <td>{refund.Method}</td>
              <td class="num">{currencyMoney(refund.CurrencyId).Format(refund.Amount)}</td>
            </tr>
            """)).ToList();

        return new Section(
            "Refunds",
            Html.Of($"<col style=\"width:100pt\"><col style=\"width:90pt\"><col><col style=\"width:100pt\">"),
            Html.Of($"<th>Refund #</th><th>Date</th><th>Method</th><th class=\"num\">Amount</th>"),
            rows,
            4);
    }
}
