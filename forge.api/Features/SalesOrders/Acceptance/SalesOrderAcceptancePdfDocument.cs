using Forge.Api.Services;
using Forge.Api.Services.Pdf;
using Forge.Core.Models;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// A compact "Sales Order Acceptance" PDF sent to the customer for e-signature. Purpose-built (not
/// coupled to the customer-PO doc) so it's always renderable from the SO itself — header, line summary,
/// total, and a customer acceptance/signature block.
/// </summary>
public static class SalesOrderAcceptancePdfDocument
{
    private const string Styles = """
        .acceptance-head .company { font-size: 16pt; font-weight: 600; }
        .acceptance-head .title { font-size: 13pt; color: #616161; }
        .acceptance-head .order { padding-top: 4pt; white-space: pre-wrap; }
        .acceptance-body { padding: 16pt 0; }
        table.summary { width: 100%; border-collapse: collapse; }
        table.summary thead { display: table-header-group; }
        table.summary th { font-weight: 600; text-align: left; padding: 0; }
        table.summary td { padding: 0; vertical-align: top; }
        table.summary tr { break-inside: avoid; }
        table.summary .num { text-align: right; white-space: nowrap; }
        table.summary .total td { padding-top: 6pt; font-weight: 600; }
        .terms-text { padding-top: 16pt; font-size: 8pt; color: #757575; white-space: pre-wrap; }
        .consent { padding-top: 28pt; font-size: 9pt; }
        .signatures { display: flex; padding-top: 28pt; break-inside: avoid; }
        .signatures .field { flex: 1; border-top: 1pt solid #000; font-size: 8pt; }
        .signatures .spacer { width: 30pt; flex: none; }
        """;

    public record Line(string Description, decimal Quantity, decimal UnitPrice);

    public static HtmlPdfDocument Build(
        string companyName, string orderNumber, string customerName, string? termsText,
        IReadOnlyList<Line> lines,
        MoneyFormatter? money = null)
    {
        money ??= MoneyFormat.For(null, "USD");

        var body = Html.Of($"""
            <div class="acceptance-head">
              <div class="company">{companyName}</div>
              <div class="title">Sales Order Acceptance</div>
              <div class="order">Order {orderNumber}  ·  {customerName}</div>
            </div>
            <div class="acceptance-body">
              {Lines(lines, money)}
              {(string.IsNullOrWhiteSpace(termsText) ? Html.Empty : Html.Of($"<div class=\"terms-text\">{termsText}</div>"))}
              <div class="consent">By signing below, the customer accepts the terms of this Sales Order and authorizes production.</div>
              <div class="signatures">
                <div class="field">Customer signature</div>
                <div class="spacer"></div>
                <div class="field">Date</div>
              </div>
            </div>
            """);
        return PdfHtml.Document($"Sales Order Acceptance {orderNumber}", body, orderNumber, styles: Styles);
    }

    private static Html Lines(IReadOnlyList<Line> lines, MoneyFormatter money)
    {
        var total = 0m;
        var rows = new List<Html>();
        foreach (var l in lines)
        {
            var amount = l.Quantity * l.UnitPrice;
            total += amount;
            rows.Add(Html.Of($"""
                <tr>
                  <td>{l.Description}</td>
                  <td class="num">{MoneyFormat.Quantity(l.Quantity)}</td>
                  <td class="num">{money.Format(l.UnitPrice)}</td>
                  <td class="num">{money.Format(amount)}</td>
                </tr>
                """));
        }

        return Html.Of($"""
            <table class="summary">
              <colgroup><col style="width:50%"><col style="width:10%"><col style="width:20%"><col style="width:20%"></colgroup>
              <thead><tr><th>Description</th><th class="num">Qty</th><th class="num">Unit</th><th class="num">Amount</th></tr></thead>
              <tbody>
                {rows}
                <tr class="total"><td colspan="3" class="num">Total</td><td class="num">{money.Format(total)}</td></tr>
              </tbody>
            </table>
            """);
    }
}
