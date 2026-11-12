using Forge.Api.Services;
using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipments;

public static class PackingSlipPdfDocument
{
    private const string Styles = """
        .head .title { color: #00796b; }
        .head .company { flex: 1; }
        .head .slip { flex: none; padding-right: 10pt; }
        .master-qr { width: 82pt; }
        .master-qr img { display: block; width: 78pt; height: 78pt; }
        .qr-caption { font-size: 7pt; color: #757575; text-align: center; }
        table.lines thead tr { background: #00796b; }
        .hazmat { font-weight: 700; color: #d32f2f; }
        .customs-note { margin-top: 6pt; font-size: 8pt; color: #757575; }
        .summary { margin-top: 20pt; }
        .summary .lines-count { font-size: 9pt; color: #757575; }
        .scan-codes { margin-top: 16pt; break-inside: avoid; }
        .scan-codes .codes { display: flex; margin-top: 4pt; }
        .scan-codes .code { width: 90pt; }
        .scan-codes .code img { display: block; width: 70pt; height: 70pt; }
        .signatures { display: flex; margin-top: 40pt; break-inside: avoid; }
        .signatures .line { flex: none; width: 46%; border-top: 1pt solid #9e9e9e; padding-top: 4pt; font-size: 9pt; color: #757575; }
        .signatures .line + .line { margin-left: 8%; }
        """;

    public static HtmlPdfDocument Build(Shipment shipment, string? companyName, ShipmentCustoms? customs = null)
    {
        var body = Html.Of($"{Header(shipment, companyName)}{Lines(shipment, customs)}{Summary(shipment, customs)}{ScanCodes(shipment)}{Notes(shipment)}{Signatures()}");
        return PdfHtml.Document($"Packing Slip {shipment.ShipmentNumber}", body, shipment.ShipmentNumber, styles: Styles);
    }

    private static Html Header(Shipment shipment, string? companyName)
    {
        var master = shipment.ScanCode
            ?? ShipmentScanCode.Compute(shipment.ShipmentNumber, shipment.Lines);
        var customer = shipment.SalesOrder.Customer;
        var addr = shipment.ShippingAddress;
        var address = addr is null
            ? Html.Empty
            : Html.Of($"""
                <div>{addr.Line1}</div>
                {(string.IsNullOrEmpty(addr.Line2) ? Html.Empty : Html.Of($"<div>{addr.Line2}</div>"))}
                <div>{addr.City}, {addr.State} {addr.PostalCode}</div>
                <div>{addr.Country}</div>
                """);

        return Html.Of($"""
            <div class="head">
              <div class="company">{companyName}</div>
              <div class="slip right"><div class="title">PACKING SLIP</div><div class="number">#{shipment.ShipmentNumber}</div></div>
              <div class="master-qr">{Html.Image(QrCodeRenderer.Png(master), cssClass: "qr")}<div class="qr-caption">SCAN TO SHIP</div></div>
            </div>
            <hr class="rule">
            <div class="parties">
              <div>
                <div class="label">Ship To:</div>
                <div class="strong">{customer.CompanyName ?? customer.Name}</div>
                {address}
              </div>
              <div class="right">
                <div>SO #: {shipment.SalesOrder.OrderNumber}</div>
                {(shipment.ShippedDate.HasValue ? Html.Of($"<div>Ship Date: {shipment.ShippedDate.Value:MMM dd, yyyy}</div>") : Html.Empty)}
                {(string.IsNullOrEmpty(shipment.Carrier) ? Html.Empty : Html.Of($"<div>Carrier: {shipment.Carrier}</div>"))}
                {(string.IsNullOrEmpty(shipment.TrackingNumber) ? Html.Empty : Html.Of($"<div>Tracking: {shipment.TrackingNumber}</div>"))}
                {(shipment.Weight.HasValue ? Html.Of($"<div>Weight: {shipment.Weight.Value:F1} lbs</div>") : Html.Empty)}
              </div>
            </div>
            """);
    }

    private static Html Lines(Shipment shipment, ShipmentCustoms? customs)
    {
        var rows = shipment.Lines
            .OrderBy(l => l.Id)
            .Select((line, i) =>
            {
                var description = line.SalesOrderLine?.Description ?? line.Part?.Description ?? "—";
                var partNumber = line.SalesOrderLine?.Part?.PartNumber ?? line.Part?.PartNumber ?? "—";
                var hazmatClass = ShipmentLineHazmat.ClassFor(line);
                var customsCells = Html.Empty;
                if (customs is not null)
                {
                    var info = customs.For(line);
                    customsCells = Html.Of($"<td>{ShipmentCustoms.Display(info.HtsCode)}</td><td>{ShipmentCustoms.Display(info.CountryOfOrigin)}</td>");
                }

                return Html.Of($"""
                    <tr>
                      <td>{i + 1}</td>
                      <td><div>{description}</div>{(hazmatClass is null ? Html.Empty : Html.Of($"<div class=\"hazmat\">HAZMAT class {hazmatClass}</div>"))}</td>
                      <td>{partNumber}</td>
                      {customsCells}
                      <td class="num">{line.Quantity}</td>
                    </tr>
                    """);
            });

        var customsCols = customs is null ? Html.Empty : Html.Of($"<col style=\"width:75pt\"><col style=\"width:45pt\">");
        var customsHeads = customs is null ? Html.Empty : Html.Of($"<th>HTS Code</th><th>Origin</th>");

        return Html.Of($"""
            <table class="lines">
              <colgroup><col style="width:40pt"><col><col style="width:80pt">{customsCols}<col style="width:60pt"></colgroup>
              <thead><tr><th>#</th><th>Description</th><th>Part #</th>{customsHeads}<th class="num">Qty</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html Summary(Shipment shipment, ShipmentCustoms? customs)
    {
        var customsNote = customs is null
            ? Html.Empty
            : Html.Of($"<div class=\"customs-note\">International shipment {customs.ShipFromCountry} to {customs.ShipToCountry}: HTS code and country of origin per line.</div>");

        return Html.Of($"""
            {customsNote}
            <div class="summary">
              <div class="strong">Total Items: {shipment.Lines.Sum(l => l.Quantity)}</div>
              <div class="lines-count">Total Lines: {shipment.Lines.Count}</div>
            </div>
            """);
    }

    private static Html ScanCodes(Shipment shipment)
    {
        var groups = shipment.Lines
            .GroupBy(l => l.SalesOrderLine?.SalesOrderId ?? shipment.SalesOrderId)
            .OrderBy(g => g.Key)
            .ToList();
        if (groups.Count == 0)
            return Html.Empty;

        var codes = groups.Select(group =>
        {
            var label = group.Key == shipment.SalesOrderId
                ? shipment.SalesOrder.OrderNumber
                : $"SO #{group.Key}";
            var code = ShipmentScanCode.ComputeForScope(shipment.ShipmentNumber, $"S{group.Key}", group);
            return Html.Of($"<div class=\"code\">{Html.Image(QrCodeRenderer.Png(code), cssClass: "qr")}<div class=\"qr-caption\">{label}</div></div>");
        });

        return Html.Of($"""
            <div class="scan-codes">
              <div class="label">Order scan codes</div>
              <div class="codes">{codes}</div>
            </div>
            """);
    }

    private static Html Notes(Shipment shipment)
        => string.IsNullOrWhiteSpace(shipment.Notes)
            ? Html.Empty
            : Html.Of($"<div class=\"notes\"><div class=\"label\">Notes</div><div class=\"body\">{shipment.Notes}</div></div>");

    private static Html Signatures()
        => Html.Of($"<div class=\"signatures\"><div class=\"line\">Received By</div><div class=\"line\">Date</div></div>");
}
