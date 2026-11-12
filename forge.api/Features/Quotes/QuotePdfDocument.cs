using Forge.Api.Services;
using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Quotes;

/// <summary>
/// S3 — the PDF attached to the quote email. Built like
/// <see cref="Invoices.InvoicePdfDocument"/> (header / customer / lines /
/// totals) plus a terms-and-conditions appendix rendering the compiled
/// sections. Terms bodies are rendered as plain paragraphs (no markdown
/// package is referenced in this solution — see TermsCompilationService).
/// </summary>
public static class QuotePdfDocument
{
    private const string Styles = """
        .terms { margin-top: 20pt; border-top: 1pt solid #bdbdbd; }
        .terms .heading { margin-top: 8pt; font-size: 13pt; font-weight: 700; color: #1976d2; }
        .terms .section-title { margin-top: 10pt; font-size: 10pt; font-weight: 600; }
        .terms .paragraph { margin-top: 3pt; font-size: 8.5pt; color: #424242; white-space: pre-wrap; }
        """;

    public static HtmlPdfDocument Build(
        Quote quote, string? companyName, IReadOnlyList<CompiledTermsSection> termsSections, MoneyFormatter? money = null)
    {
        money ??= MoneyFormat.For(null, "USD");
        var number = quote.QuoteNumber ?? quote.Id.ToString();

        var body = Html.Of($"{Header(quote, number, companyName)}{Lines(quote, money)}{Totals(quote, money)}{Notes(quote)}{Terms(termsSections)}");
        return PdfHtml.Document($"Quote {number}", body, number, styles: Styles);
    }

    private static Html Header(Quote quote, string number, string? companyName)
    {
        var customer = quote.Customer;
        return Html.Of($"""
            <div class="head">
              <div class="company">{(string.IsNullOrWhiteSpace(companyName) ? null : companyName)}</div>
              <div class="right"><div class="title">QUOTE</div><div class="number">{number}</div></div>
            </div>
            <hr class="rule">
            <div class="parties">
              <div>
                <div class="label">Prepared For:</div>
                <div class="strong">{customer.CompanyName ?? customer.Name}</div>
                {(string.IsNullOrEmpty(customer.Email) ? Html.Empty : Html.Of($"<div class=\"small\">{customer.Email}</div>"))}
                {(string.IsNullOrEmpty(customer.Phone) ? Html.Empty : Html.Of($"<div class=\"small\">{customer.Phone}</div>"))}
              </div>
              <div class="right">
                <div>Quote Date: {quote.CreatedAt:MM/dd/yyyy}</div>
                {(quote.ExpirationDate.HasValue ? Html.Of($"<div>Valid Until: {quote.ExpirationDate:MM/dd/yyyy}</div>") : Html.Empty)}
                {(string.IsNullOrEmpty(quote.CustomerPO) ? Html.Empty : Html.Of($"<div>Customer PO: {quote.CustomerPO}</div>"))}
              </div>
            </div>
            """);
    }

    private static Html Lines(Quote quote, MoneyFormatter money)
    {
        var rows = quote.Lines
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

    private static Html Totals(Quote quote, MoneyFormatter money)
    {
        var rows = new List<Html>
        {
            Row("Subtotal:", money.Format(quote.Subtotal - quote.FreightSubtotal), "strong-label"),
        };
        if (quote.Lines.Any(l => l.IsFreight))
            rows.Add(Row("Freight:", money.Format(quote.FreightSubtotal), "strong-label"));
        if (quote.TaxRate > 0)
            rows.Add(Row($"Tax ({MoneyFormat.Percent(quote.TaxRate)}):", money.Format(quote.TaxAmount), "strong-label"));
        if (quote.TaxOverrideReason is { } taxOverrideReason)
        {
            rows.Add(Html.Of(
                $"<div class=\"note\">Tax exemption: {TaxOverrideActivity.ReasonLabel(taxOverrideReason)}{(quote.TaxOverrideNote is null ? string.Empty : $" ({quote.TaxOverrideNote})")}</div>"));
        }

        rows.Add(Row("Total:", money.Format(quote.Total), "grand after-rule"));

        return Html.Of($"<div class=\"totals\">{rows}</div>");
    }

    private static Html Row(string label, string amount, string style)
        => Html.Of($"<div class=\"row {style}\"><span>{label}</span><span>{amount}</span></div>");

    private static Html Notes(Quote quote)
        => string.IsNullOrWhiteSpace(quote.Notes)
            ? Html.Empty
            : Html.Of($"<div class=\"notes\"><div class=\"label\">Notes</div><div class=\"body\">{quote.Notes}</div></div>");

    private static Html Terms(IReadOnlyList<CompiledTermsSection> termsSections)
    {
        if (termsSections.Count == 0)
            return Html.Empty;

        var sections = termsSections.Select(section => Html.Of($"""
            <div class="section-title">{section.Title}</div>
            {SplitParagraphs(section.BodyMarkdown).Select(paragraph => Html.Of($"<div class=\"paragraph\">{paragraph}</div>"))}
            """));

        return Html.Of($"""
            <div class="terms">
              <div class="heading">Terms &amp; Conditions</div>
              {sections}
            </div>
            """);
    }

    private static IEnumerable<string> SplitParagraphs(string markdown) =>
        markdown.Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
