using System.Globalization;

using Forge.Api.Features.VendorCredits;
using Forge.Api.Services;
using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.VendorReturns;

/// <summary>The return-to-vendor packing document: what goes back, from which lot and bin, and the credit expected.</summary>
public static class VendorReturnPdfDocument
{
    private const string Styles = """
        .head .title { font-size: 20pt; }
        .totals.expected { width: 240pt; }
        .totals.expected .row { font-weight: 700; }
        .signatures { break-inside: avoid; }
        .signatures .packed { margin-top: 40pt; }
        .signatures .received { margin-top: 20pt; }
        """;

    public static HtmlPdfDocument Build(VendorReturn rtv, string? companyName, MoneyFormatter money, VendorDocumentPdfLabels labels)
    {
        var body = Html.Of($"{Header(rtv, companyName, labels)}{Lines(rtv, money, labels)}{ExpectedCredit(rtv, money, labels)}{Notes(rtv, labels)}{Signatures(labels)}");
        return PdfHtml.Document(
            $"{labels["rtv"]} {rtv.ReturnNumber}", body, rtv.ReturnNumber, styles: Styles,
            pageWords: new PdfPageWords($"{labels["page"]} ", $" {labels["of"]} "));
    }

    private static Html Header(VendorReturn rtv, string? companyName, VendorDocumentPdfLabels labels)
    {
        var shipDate = rtv.ShipDate is { } date
            ? Html.Of($"<div>{labels["shipDate"]} {date.ToString(labels.DateFormat, CultureInfo.InvariantCulture)}</div>")
            : Html.Empty;

        return Html.Of($"""
            <div class="head">
              <div class="company">{companyName}</div>
              <div class="right"><div class="title">{labels["rtv"]}</div><div class="number">{rtv.ReturnNumber}</div></div>
            </div>
            <hr class="rule">
            <div class="parties">
              <div>
                <div class="label">{labels["shipTo"]}</div>
                <div class="strong">{rtv.Vendor.CompanyName}</div>
                {(string.IsNullOrWhiteSpace(rtv.Vendor.Address) ? Html.Empty : Html.Of($"<div class=\"small\">{rtv.Vendor.Address}</div>"))}
              </div>
              <div class="right">
                {(rtv.NonConformance is null ? Html.Empty : Html.Of($"<div>{labels["ncr"]} {rtv.NonConformance.NcrNumber}</div>"))}
                {(rtv.PurchaseOrder is null ? Html.Empty : Html.Of($"<div>{labels["po"]} {rtv.PurchaseOrder.PONumber}</div>"))}
                {(string.IsNullOrWhiteSpace(rtv.RmaNumber) ? Html.Empty : Html.Of($"<div>{labels["rma"]} {rtv.RmaNumber}</div>"))}
                {shipDate}
                {(string.IsNullOrWhiteSpace(rtv.Carrier) ? Html.Empty : Html.Of($"<div>{labels["carrier"]} {rtv.Carrier}</div>"))}
                {(string.IsNullOrWhiteSpace(rtv.TrackingNumber) ? Html.Empty : Html.Of($"<div>{labels["tracking"]} {rtv.TrackingNumber}</div>"))}
              </div>
            </div>
            """);
    }

    private static Html Lines(VendorReturn rtv, MoneyFormatter money, VendorDocumentPdfLabels labels)
    {
        var rows = rtv.Lines
            .OrderBy(l => l.LineNumber)
            .Select(line => Html.Of($"""
                <tr>
                  <td>{line.LineNumber}</td>
                  <td>{(line.Part.Description is { Length: > 0 } d ? $"{line.Part.PartNumber} — {d}" : line.Part.PartNumber)}</td>
                  <td>{line.LotNumber ?? string.Empty}</td>
                  <td>{line.Location?.Name ?? string.Empty}</td>
                  <td class="num">{MoneyFormat.Quantity(line.Quantity)}</td>
                  <td class="num">{money.Format(line.UnitPrice)}</td>
                </tr>
                """));

        return Html.Of($"""
            <table class="lines">
              <colgroup><col style="width:30pt"><col style="width:176pt"><col style="width:88pt"><col style="width:88pt"><col style="width:55pt"><col style="width:75pt"></colgroup>
              <thead><tr><th>{labels["number"]}</th><th>{labels["part"]}</th><th>{labels["lot"]}</th><th>{labels["location"]}</th><th class="num">{labels["qty"]}</th><th class="num">{labels["unitPrice"]}</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html ExpectedCredit(VendorReturn rtv, MoneyFormatter money, VendorDocumentPdfLabels labels)
        => Html.Of($"<div class=\"totals expected\"><div class=\"row\"><span>{labels["expectedCredit"]}</span><span>{money.Format(rtv.ExpectedCredit)}</span></div></div>");

    private static Html Notes(VendorReturn rtv, VendorDocumentPdfLabels labels)
        => string.IsNullOrWhiteSpace(rtv.Notes)
            ? Html.Empty
            : Html.Of($"<div class=\"notes\"><div class=\"label\">{labels["notes"]}</div><div class=\"body\">{rtv.Notes}</div></div>");

    private static Html Signatures(VendorDocumentPdfLabels labels) => Html.Of($"""
        <div class="signatures">
          <div class="packed">{labels["packedBy"]}</div>
          <div class="received">{labels["receivedBy"]}</div>
        </div>
        """);
}
