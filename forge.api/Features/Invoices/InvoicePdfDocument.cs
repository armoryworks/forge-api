using Forge.Api.Services;
using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Invoices;

public static class InvoicePdfDocument
{
    public static HtmlPdfDocument Build(Invoice invoice, string? companyName, MoneyFormatter? money = null, InvoiceDiscountOffer? offer = null)
    {
        money ??= MoneyFormat.For(null, "USD");
        offer ??= InvoiceDiscountOffer.None;

        var body = Html.Of($"{Header(invoice, companyName, money, offer)}{Lines(invoice, money)}{Totals(invoice, money)}{Notes(invoice)}");
        return PdfHtml.Document($"Invoice {invoice.InvoiceNumber}", body, invoice.InvoiceNumber);
    }

    private static Html Header(Invoice invoice, string? companyName, MoneyFormatter money, InvoiceDiscountOffer offer)
    {
        var customer = invoice.Customer;
        var terms = offer.TermsName is { Length: > 0 } termsName ? termsName
            : invoice.CreditTerms.HasValue ? FormatCreditTerms(invoice.CreditTerms.Value)
            : null;

        return Html.Of($"""
            <div class="head">
              <div class="company">{companyName}</div>
              <div class="right"><div class="title">INVOICE</div><div class="number">{invoice.InvoiceNumber}</div></div>
            </div>
            <hr class="rule">
            <div class="parties">
              <div>
                <div class="label">Bill To:</div>
                <div class="strong">{customer.CompanyName ?? customer.Name}</div>
                {(string.IsNullOrEmpty(customer.Email) ? Html.Empty : Html.Of($"<div class=\"small\">{customer.Email}</div>"))}
                {(string.IsNullOrEmpty(customer.Phone) ? Html.Empty : Html.Of($"<div class=\"small\">{customer.Phone}</div>"))}
              </div>
              <div class="right">
                <div>Invoice Date: {invoice.InvoiceDate:MMM dd, yyyy}</div>
                <div>Due Date: {invoice.DueDate:MMM dd, yyyy}</div>
                {(terms is null ? Html.Empty : Html.Of($"<div>Terms: {terms}</div>"))}
                {(offer.HasDiscount ? Html.Of($"<div class=\"strong\">Pay {money.Format(offer.PayAmount)} by {offer.Deadline:MM/dd/yyyy} to take the discount.</div>") : Html.Empty)}
                {(string.IsNullOrEmpty(invoice.SalesOrder?.OrderNumber) ? Html.Empty : Html.Of($"<div>SO #: {invoice.SalesOrder.OrderNumber}</div>"))}
              </div>
            </div>
            """);
    }

    private static Html Lines(Invoice invoice, MoneyFormatter money)
    {
        var rows = invoice.Lines
            .OrderBy(l => l.IsFreight)
            .ThenBy(l => l.LineNumber)
            .Select(line => Html.Of($"""
                <tr>
                  <td>{line.LineNumber}</td>
                  <td>{line.Description}</td>
                  <td>{line.Part?.PartNumber ?? "—"}</td>
                  <td class="num">{MoneyFormat.Quantity(line.Quantity)}</td>
                  <td class="num">{money.Format(line.UnitPrice)}</td>
                  <td class="num">{money.Format(line.LineTotal)}</td>
                </tr>
                """));

        return Html.Of($"""
            <table class="lines">
              <colgroup><col style="width:40pt"><col><col style="width:80pt"><col style="width:60pt"><col style="width:80pt"><col style="width:90pt"></colgroup>
              <thead><tr><th>#</th><th>Description</th><th>Part #</th><th class="num">Qty</th><th class="num">Unit Price</th><th class="num">Total</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html Totals(Invoice invoice, MoneyFormatter money)
    {
        var rows = new List<Html>
        {
            Row("Subtotal:", money.Format(invoice.Subtotal - invoice.FreightSubtotal), "strong-label"),
        };
        if (invoice.Lines.Any(l => l.IsFreight))
            rows.Add(Row("Freight:", money.Format(invoice.FreightSubtotal), "strong-label"));
        if (invoice.TaxRate > 0)
            rows.Add(Row($"Tax ({MoneyFormat.Percent(invoice.TaxRate)}):", money.Format(invoice.TaxAmount), "strong-label"));
        if (invoice.TaxOverrideReason is { } taxOverrideReason)
        {
            rows.Add(Html.Of(
                $"<div class=\"note\">Tax exemption: {TaxOverrideActivity.ReasonLabel(taxOverrideReason)}{(invoice.TaxOverrideNote is null ? string.Empty : $" ({invoice.TaxOverrideNote})")}</div>"));
        }

        rows.Add(Row("Total:", money.Format(invoice.Total), "grand after-rule"));

        if (invoice.AmountPaid > 0 || invoice.CreditsApplied > 0)
        {
            if (invoice.AmountPaid > 0)
                rows.Add(Row("Paid:", $"({money.Format(invoice.AmountPaid)})", "credit"));
            if (invoice.CreditsApplied > 0)
                rows.Add(Row("Credits applied:", $"({money.Format(invoice.CreditsApplied)})", "credit"));
            if (invoice.DiscountsTaken > 0)
                rows.Add(Row("Early-payment discount:", $"({money.Format(invoice.DiscountsTaken)})", "credit"));
            rows.Add(Row("Balance Due:", money.Format(invoice.BalanceDue), "grand"));
        }

        return Html.Of($"<div class=\"totals\">{rows}</div>");
    }

    private static Html Row(string label, string amount, string style)
        => Html.Of($"<div class=\"row {style}\"><span>{label}</span><span>{amount}</span></div>");

    private static Html Notes(Invoice invoice)
        => string.IsNullOrWhiteSpace(invoice.Notes)
            ? Html.Empty
            : Html.Of($"<div class=\"notes\"><div class=\"label\">Notes</div><div class=\"body\">{invoice.Notes}</div></div>");

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
