using System.Text.RegularExpressions;

using Forge.Api.Services;
using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// The combined "wrapped" ship document: a landscape page with the carrier's shipping label on the
/// left, and the company details + Forge master QR (scan-to-ship) + a color-coded, symbolic carrier
/// badge on the right so the carrier is unmistakable at a glance. A cross-border shipment also lists
/// each line's HTS code and country of origin.
/// </summary>
public static partial class ShipDocumentPdfDocument
{
    private const string NeutralPrimary = "#37474F";
    private const string NeutralAccent = "#90A4AE";

    private const string Styles = """
        @page { margin: 26pt 26pt 40pt; }
        body { font-size: 11pt; }
        .sheet { display: flex; align-items: flex-start; }
        .label-frame { flex: 5; height: 540pt; border: 1pt solid #bdbdbd; padding: 12pt; }
        .label-frame img { display: block; width: 100%; height: 100%; object-fit: contain; }
        .side { flex: 4; margin-left: 22pt; }
        .badge { padding: 14pt; color: #fff; }
        .badge .carrier { font-size: 30pt; font-weight: 700; line-height: 1.1; }
        .badge .via { font-size: 9pt; letter-spacing: 0.2em; }
        .company { margin-top: 16pt; font-size: 18pt; font-weight: 700; }
        .muted { color: #616161; }
        .side .rule { margin: 14pt 0 0; }
        .shipment { margin-top: 12pt; font-size: 14pt; font-weight: 700; }
        .customs-title { margin-top: 10pt; font-size: 10pt; font-weight: 700; }
        .customs-line { font-size: 9pt; }
        .scan { display: flex; align-items: flex-end; margin-top: 18pt; }
        .scan .qr-block { width: 120pt; }
        .scan .qr-block img { display: block; width: 112pt; height: 112pt; }
        .scan .caption { font-size: 8pt; color: #757575; text-align: center; }
        .scan .by { flex: 1; padding-left: 10pt; font-size: 9pt; color: #9e9e9e; }
        """;

    public static HtmlPdfDocument Build(
        Shipment shipment,
        byte[] labelPng,
        string companyName,
        string? companyAddress,
        string? companyPhone,
        CarrierBadgeStyle carrier,
        ShipmentCustoms? customs = null)
    {
        var master = shipment.ScanCode
            ?? ShipmentScanCode.Compute(shipment.ShipmentNumber, shipment.Lines);
        var badgeStyle = $"background: {Color(carrier.Primary, NeutralPrimary)}; border-left: 10pt solid {Color(carrier.Accent, NeutralAccent)};";

        var body = Html.Of($"""
            <div class="sheet">
              <div class="label-frame">{Html.Image(labelPng, cssClass: "label")}</div>
              <div class="side">
                <div class="badge" style="{badgeStyle}">
                  <div class="carrier">{carrier.Label}</div>
                  <div class="via">SHIP VIA</div>
                </div>
                <div class="company">{companyName}</div>
                {(string.IsNullOrWhiteSpace(companyAddress) ? Html.Empty : Html.Of($"<div class=\"muted\">{companyAddress}</div>"))}
                {(string.IsNullOrWhiteSpace(companyPhone) ? Html.Empty : Html.Of($"<div class=\"muted\">{companyPhone}</div>"))}
                <hr class="rule">
                <div class="shipment">Shipment {shipment.ShipmentNumber}</div>
                <div>Order {shipment.SalesOrder.OrderNumber}</div>
                <div>Ship to: {shipment.SalesOrder.Customer.Name}</div>
                {(string.IsNullOrWhiteSpace(shipment.TrackingNumber) ? Html.Empty : Html.Of($"<div class=\"muted\">Tracking: {shipment.TrackingNumber}</div>"))}
                {Customs(shipment, customs)}
                <div class="scan">
                  <div class="qr-block">{Html.Image(QrCodeRenderer.Png(master), cssClass: "qr")}<div class="caption">SCAN TO SHIP</div></div>
                  <div class="by">{companyName}</div>
                </div>
              </div>
            </div>
            """);

        return PdfHtml.Document($"Ship Document {shipment.ShipmentNumber}", body, shipment.ShipmentNumber, landscape: true, styles: Styles);
    }

    private static Html Customs(Shipment shipment, ShipmentCustoms? customs)
    {
        if (customs is null)
            return Html.Empty;

        var lines = shipment.Lines.OrderBy(l => l.Id).Select(line =>
        {
            var info = customs.For(line);
            var label = line.Part?.PartNumber ?? line.SalesOrderLine?.Part?.PartNumber
                ?? line.SalesOrderLine?.Description ?? line.Description ?? "—";
            return Html.Of($"<div class=\"customs-line\">{label} × {line.Quantity} · HTS {ShipmentCustoms.Display(info.HtsCode)} · Origin {ShipmentCustoms.Display(info.CountryOfOrigin)}</div>");
        });

        return Html.Of($"<div class=\"customs-title\">Customs: {customs.ShipFromCountry} to {customs.ShipToCountry}</div>{lines}");
    }

    private static string Color(string value, string fallback)
        => HexColor().IsMatch(value) ? value : fallback;

    [GeneratedRegex("^#[0-9A-Fa-f]{3,8}$")]
    private static partial Regex HexColor();
}
