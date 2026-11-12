using System.Globalization;

using Forge.Api.Services;
using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.CreditMemos;

/// <summary>The customer-facing credit memo: lines, tax, total, what has been applied or refunded and what is left.</summary>
public static class CreditMemoPdfDocument
{
    private const string Styles = """
        .voided { font-weight: 700; color: #d32f2f; }
        """;

    private const string TranslatedFooterStyles = """
        @page { @bottom-center { content: element(translated-footer); } }
        .translated-footer { position: running(translated-footer); font-size: 8pt; color: #9e9e9e; text-align: center; }
        .translated-footer .page::after { content: counter(page); }
        .translated-footer .pages::after { content: counter(pages); }
        """;

    public static HtmlPdfDocument Build(
        CreditMemo memo, string? companyName, MoneyFormatter money, CreditMemoPdfLabels? labels = null)
    {
        labels ??= CreditMemoPdfLabels.English;
        var translatedFooter = labels.Page != CreditMemoPdfLabels.English.Page || labels.Of != CreditMemoPdfLabels.English.Of;

        var body = Html.Of($"{(translatedFooter ? TranslatedFooter(memo, labels) : Html.Empty)}{Header(memo, companyName, labels)}{Lines(memo, money, labels)}{Totals(memo, money, labels)}{Notes(memo, labels)}");
        return PdfHtml.Document(
            $"{labels.Title} {memo.CreditMemoNumber}", body, memo.CreditMemoNumber,
            styles: translatedFooter ? Styles + TranslatedFooterStyles : Styles);
    }

    private static Html TranslatedFooter(CreditMemo memo, CreditMemoPdfLabels labels)
        => Html.Of($"<div class=\"translated-footer\">{memo.CreditMemoNumber} &middot; {labels.Page}<span class=\"page\"></span>{labels.Of}<span class=\"pages\"></span></div>");

    private static Html Header(CreditMemo memo, string? companyName, CreditMemoPdfLabels labels)
    {
        var customer = memo.Customer;
        return Html.Of($"""
            <div class="head">
              <div class="company">{companyName}</div>
              <div class="right"><div class="title">{labels.Title}</div><div class="number">{memo.CreditMemoNumber}</div></div>
            </div>
            <hr class="rule">
            <div class="parties">
              <div>
                <div class="label">{labels.CreditTo}</div>
                <div class="strong">{customer.CompanyName ?? customer.Name}</div>
                {(string.IsNullOrEmpty(customer.Email) ? Html.Empty : Html.Of($"<div class=\"small\">{customer.Email}</div>"))}
              </div>
              <div class="right">
                <div>{labels.CreditDate} {memo.CreditDate.ToString(labels.DateFormat, CultureInfo.InvariantCulture)}</div>
                {(memo.Invoice is null ? Html.Empty : Html.Of($"<div>{labels.InvoiceNumber} {memo.Invoice.InvoiceNumber}</div>"))}
                <div>{labels.Reason} {memo.Reason}</div>
                {(memo.Status == CreditMemoStatus.Voided ? Html.Of($"<div class=\"voided\">{labels.Voided}</div>") : Html.Empty)}
              </div>
            </div>
            """);
    }

    private static Html Lines(CreditMemo memo, MoneyFormatter money, CreditMemoPdfLabels labels)
    {
        var rows = memo.Lines
            .OrderBy(l => l.LineNumber)
            .Select(line => Html.Of($"""
                <tr>
                  <td>{line.LineNumber}</td>
                  <td>{line.Description}</td>
                  <td class="num">{MoneyFormat.Quantity(line.Quantity)}</td>
                  <td class="num">{money.Format(line.UnitPrice)}</td>
                  <td class="num">{money.Format(line.SignedTotal)}</td>
                </tr>
                """));

        return Html.Of($"""
            <table class="lines">
              <colgroup><col style="width:40pt"><col><col style="width:60pt"><col style="width:80pt"><col style="width:90pt"></colgroup>
              <thead><tr><th>#</th><th>{labels.Description}</th><th class="num">{labels.Quantity}</th><th class="num">{labels.UnitPrice}</th><th class="num">{labels.Credit}</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html Totals(CreditMemo memo, MoneyFormatter money, CreditMemoPdfLabels labels)
    {
        var rows = new List<Html>
        {
            Row(labels.Subtotal, money.Format(memo.Subtotal), "strong-label"),
        };
        if (memo.TaxAmount != 0m)
            rows.Add(Row(string.Format(CultureInfo.InvariantCulture, labels.Tax, MoneyFormat.Percent(memo.TaxRate)), money.Format(memo.TaxAmount), "strong-label"));
        rows.Add(Row(labels.TotalCredit, money.Format(memo.Total), "grand after-rule"));
        if (memo.AmountApplied > 0m)
            rows.Add(Row(labels.AppliedToInvoices, money.Format(-memo.AmountApplied), "strong-label"));
        if (memo.AmountRefunded > 0m)
            rows.Add(Row(labels.Refunded, money.Format(-memo.AmountRefunded), "strong-label"));
        if (memo.AmountApplied > 0m || memo.AmountRefunded > 0m)
            rows.Add(Row(labels.CreditRemaining, money.Format(memo.OpenAmount), "strong-label"));

        return Html.Of($"<div class=\"totals\">{rows}</div>");
    }

    private static Html Row(string label, string amount, string style)
        => Html.Of($"<div class=\"row {style}\"><span>{label}</span><span>{amount}</span></div>");

    private static Html Notes(CreditMemo memo, CreditMemoPdfLabels labels)
        => string.IsNullOrWhiteSpace(memo.Notes)
            ? Html.Empty
            : Html.Of($"<div class=\"notes\"><div class=\"label\">{labels.Notes}</div><div class=\"body\">{memo.Notes}</div></div>");
}
