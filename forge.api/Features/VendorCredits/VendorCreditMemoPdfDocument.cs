using System.Globalization;

using Forge.Api.Services;
using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.VendorCredits;

/// <summary>The vendor credit or debit memo document: lines, tax, total, what has been applied to bills and what is left.</summary>
public static class VendorCreditMemoPdfDocument
{
    private const string Styles = """
        .head .title { font-size: 20pt; }
        .voided { font-weight: 700; color: #d32f2f; }
        """;

    public static HtmlPdfDocument Build(VendorCreditMemo memo, string? companyName, MoneyFormatter money, VendorDocumentPdfLabels labels)
    {
        var title = memo.Kind == VendorCreditMemoKind.DebitMemo ? labels["debitMemo"] : labels["creditMemo"];
        var body = Html.Of($"{Header(memo, companyName, title, labels)}{Lines(memo, money, labels)}{Totals(memo, money, labels)}{Notes(memo, labels)}");
        return PdfHtml.Document(
            $"{title} {memo.MemoNumber}", body, memo.MemoNumber, styles: Styles,
            pageWords: new PdfPageWords($"{labels["page"]} ", $" {labels["of"]} "));
    }

    private static Html Header(VendorCreditMemo memo, string? companyName, string title, VendorDocumentPdfLabels labels)
    {
        var reference = string.IsNullOrWhiteSpace(memo.VendorReference)
            ? Html.Empty
            : Html.Of($"<div>{labels["vendorReference"]} {memo.VendorReference}</div>");

        return Html.Of($"""
            <div class="head">
              <div class="company">{companyName}</div>
              <div class="right"><div class="title">{title}</div><div class="number">{memo.MemoNumber}</div></div>
            </div>
            <hr class="rule">
            <div class="parties">
              <div>
                <div class="label">{labels["vendor"]}</div>
                <div class="strong">{memo.Vendor.CompanyName}</div>
                {(string.IsNullOrEmpty(memo.Vendor.Email) ? Html.Empty : Html.Of($"<div class=\"small\">{memo.Vendor.Email}</div>"))}
              </div>
              <div class="right">
                <div>{labels["memoDate"]} {memo.MemoDate.ToString(labels.DateFormat, CultureInfo.InvariantCulture)}</div>
                {(memo.VendorBill is null ? Html.Empty : Html.Of($"<div>{labels["bill"]} {memo.VendorBill.BillNumber}</div>"))}
                {(memo.VendorReturn is null ? Html.Empty : Html.Of($"<div>{labels["return"]} {memo.VendorReturn.ReturnNumber}</div>"))}
                {reference}
                <div>{labels["reason"]} {memo.Reason}</div>
                {(memo.Status == VendorCreditMemoStatus.Voided ? Html.Of($"<div class=\"voided\">{labels["voided"]}</div>") : Html.Empty)}
              </div>
            </div>
            """);
    }

    private static Html Lines(VendorCreditMemo memo, MoneyFormatter money, VendorDocumentPdfLabels labels)
    {
        var rows = memo.Lines
            .OrderBy(l => l.LineNumber)
            .Select(line => Html.Of($"""
                <tr>
                  <td>{line.LineNumber}</td>
                  <td>{line.Description}</td>
                  <td class="num">{MoneyFormat.Quantity(line.Quantity)}</td>
                  <td class="num">{money.Format(line.UnitPrice)}</td>
                  <td class="num">{money.Format(line.LineTotal)}</td>
                </tr>
                """));

        return Html.Of($"""
            <table class="lines">
              <colgroup><col style="width:40pt"><col><col style="width:60pt"><col style="width:80pt"><col style="width:90pt"></colgroup>
              <thead><tr><th>{labels["number"]}</th><th>{labels["description"]}</th><th class="num">{labels["qty"]}</th><th class="num">{labels["unitPrice"]}</th><th class="num">{labels["amount"]}</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html Totals(VendorCreditMemo memo, MoneyFormatter money, VendorDocumentPdfLabels labels)
    {
        var rows = new List<Html> { Row(labels["subtotal"], money.Format(memo.Subtotal), "strong-label") };
        if (memo.TaxAmount != 0m)
            rows.Add(Row(labels["tax"], money.Format(memo.TaxAmount), "strong-label"));
        rows.Add(Row(labels["totalCredit"], money.Format(memo.Total), "grand after-rule"));
        if (memo.AmountApplied > 0m)
        {
            rows.Add(Row(labels["applied"], money.Format(-memo.AmountApplied), "strong-label"));
            rows.Add(Row(labels["remaining"], money.Format(memo.OpenAmount), "strong-label"));
        }

        return Html.Of($"<div class=\"totals\">{rows}</div>");
    }

    private static Html Row(string label, string amount, string style)
        => Html.Of($"<div class=\"row {style}\"><span>{label}</span><span>{amount}</span></div>");

    private static Html Notes(VendorCreditMemo memo, VendorDocumentPdfLabels labels)
        => string.IsNullOrWhiteSpace(memo.Notes)
            ? Html.Empty
            : Html.Of($"<div class=\"notes\"><div class=\"label\">{labels["notes"]}</div><div class=\"body\">{memo.Notes}</div></div>");
}
