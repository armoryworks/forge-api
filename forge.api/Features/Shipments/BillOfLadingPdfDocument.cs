using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipments;

public static class BillOfLadingPdfDocument
{
    private const string Styles = """
        @page { margin: 30pt 40pt 44pt; }
        body { font-size: 9pt; }
        .bol-head { text-align: center; }
        .bol-head .title { font-size: 20pt; font-weight: 700; line-height: 1.1; }
        .bol-head .subtitle { font-size: 8pt; color: #757575; }
        .bol-head .heavy-rule { border: 0; border-top: 2pt solid #000; margin: 6pt 0 8pt; }
        .field-label { font-size: 8pt; font-weight: 700; color: #616161; }
        .parties-boxes { display: flex; align-items: stretch; }
        .parties-boxes .box { flex: 1; }
        .parties-boxes .box + .box { margin-left: 1pt; }
        .box { border: 1pt solid #000; padding: 8pt; }
        .box .first { margin-top: 4pt; }
        .fields { display: flex; margin-top: 4pt; }
        .fields > div { flex: 1; }
        .after-boxes { margin-top: 8pt; }
        .hazmat-banner { border: 2pt solid #d32f2f; padding: 6pt; margin-bottom: 6pt; font-size: 9pt; font-weight: 700; color: #d32f2f; }
        .customs-banner { margin-bottom: 4pt; font-size: 8pt; font-weight: 600; }
        table.lines.articles thead tr { background: #000; }
        table.lines.articles th { font-size: 8pt; }
        table.lines .center { text-align: center; font-weight: 700; }
        .hazmat { font-weight: 700; color: #d32f2f; }
        .customs { font-size: 8pt; color: #616161; }
        .packages-title { margin-top: 8pt; font-size: 9pt; font-weight: 700; }
        table.lines.packages thead tr { background: #424242; }
        table.lines.packages th { font-size: 8pt; padding: 4pt; }
        table.lines.packages td { padding: 4pt; }
        table.lines.packages tbody tr:nth-child(even) { background: none; }
        .totals-box { margin-top: 12pt; break-inside: avoid; }
        .totals-box .fields { margin-top: 0; }
        .instructions { margin-top: 12pt; break-inside: avoid; }
        .instructions .body { margin-top: 4pt; white-space: pre-wrap; }
        .signatures { display: flex; break-inside: avoid; }
        .signatures.first-row { margin-top: 30pt; }
        .signatures.second-row { margin-top: 20pt; }
        .signatures .sig { flex: 1; }
        .signatures .sig + .sig { margin-left: 30pt; }
        .signatures .caption { font-size: 8pt; color: #616161; }
        .signatures .line { margin-top: 20pt; border-top: 1pt solid #9e9e9e; padding-top: 4pt; font-size: 8pt; color: #9e9e9e; }
        .page-footer { border-top: 1pt solid #bdbdbd; padding-top: 4pt; }
        .page-footer .footer-note { font-size: 7pt; margin-bottom: 4pt; }
        """;

    public static HtmlPdfDocument Build(
        Shipment shipment,
        string? companyName,
        string? companyAddress = null,
        string? companyPhone = null,
        ShipmentCustoms? customs = null)
    {
        var hasHazmat = ShipmentLineHazmat.AnyHazmat(shipment.Lines);
        var body = Html.Of($"""
            {Header()}
            {Parties(shipment, companyName, companyAddress, companyPhone)}
            {References(shipment)}
            <div class="after-boxes">{Banners(hasHazmat, customs)}</div>
            {Articles(shipment, hasHazmat, customs)}
            {Packages(shipment)}
            {TotalsBox(shipment)}
            {Instructions(shipment)}
            {Signatures()}
            """);
        var number = $"BOL-{shipment.ShipmentNumber}";
        return PdfHtml.Document(
            $"Bill of Lading {number}", body, number, styles: Styles,
            footerNote: Html.Of($"This is to certify that the above-named materials are properly classified, described, packaged, marked, and labeled, and are in proper condition for transportation."));
    }

    private static Html Header()
        => Html.Of($"""
            <div class="bol-head">
              <div class="title">BILL OF LADING</div>
              <div class="subtitle">SHORT FORM — NOT NEGOTIABLE</div>
              <hr class="heavy-rule">
            </div>
            """);

    private static Html Parties(Shipment shipment, string? companyName, string? companyAddress, string? companyPhone)
    {
        var customer = shipment.SalesOrder?.Customer;
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
            <div class="parties-boxes">
              <div class="box">
                <div class="field-label">SHIPPER (From):</div>
                {(companyName is null ? Html.Empty : Html.Of($"<div class=\"first strong\">{companyName}</div>"))}
                {(string.IsNullOrEmpty(companyAddress) ? Html.Empty : Html.Of($"<div>{companyAddress}</div>"))}
                {(string.IsNullOrEmpty(companyPhone) ? Html.Empty : Html.Of($"<div>Tel: {companyPhone}</div>"))}
              </div>
              <div class="box">
                <div class="field-label">CONSIGNEE (To):</div>
                {(customer is null ? Html.Empty : Html.Of($"<div class=\"first strong\">{customer.CompanyName ?? customer.Name}</div>"))}
                {address}
              </div>
            </div>
            """);
    }

    private static Html References(Shipment shipment)
        => Html.Of($"""
            <div class="box fields">
              <div><div class="field-label">CARRIER:</div><div class="strong">{shipment.Carrier ?? "—"}</div></div>
              <div><div class="field-label">B/L NUMBER:</div><div class="strong">BOL-{shipment.ShipmentNumber}</div></div>
              <div><div class="field-label">DATE:</div><div class="strong">{(shipment.ShippedDate ?? DateTimeOffset.UtcNow).ToString("MMM dd, yyyy")}</div></div>
            </div>
            <div class="box fields">
              <div><div class="field-label">SO NUMBER:</div><div>{shipment.SalesOrder?.OrderNumber ?? "—"}</div></div>
              <div><div class="field-label">TRACKING #:</div><div>{shipment.TrackingNumber ?? "—"}</div></div>
              <div><div class="field-label">SHIPMENT #:</div><div>{shipment.ShipmentNumber}</div></div>
            </div>
            """);

    private static Html Banners(bool hasHazmat, ShipmentCustoms? customs)
    {
        var hazmat = hasHazmat
            ? Html.Of($"<div class=\"hazmat-banner\">CONTAINS HAZARDOUS MATERIALS — lines marked X in the HM column are regulated; see the hazmat class under each.</div>")
            : Html.Empty;
        var international = customs is null
            ? Html.Empty
            : Html.Of($"<div class=\"customs-banner\">INTERNATIONAL SHIPMENT {customs.ShipFromCountry} TO {customs.ShipToCountry} — HTS code and country of origin shown under each article.</div>");
        return Html.Of($"{hazmat}{international}");
    }

    private static Html Articles(Shipment shipment, bool hasHazmat, ShipmentCustoms? customs)
    {
        var rows = shipment.Lines
            .OrderBy(l => l.Id)
            .Select((line, i) =>
            {
                var description = line.SalesOrderLine?.Description ?? line.Part?.Description ?? "—";
                var partNumber = line.SalesOrderLine?.Part?.PartNumber ?? line.Part?.PartNumber ?? "—";
                var hazmatClass = ShipmentLineHazmat.ClassFor(line);
                var customsLine = Html.Empty;
                if (customs is not null)
                {
                    var info = customs.For(line);
                    customsLine = Html.Of($"<div class=\"customs\">HTS {ShipmentCustoms.Display(info.HtsCode)} · Origin {ShipmentCustoms.Display(info.CountryOfOrigin)}</div>");
                }

                return Html.Of($"""
                    <tr>
                      {(hasHazmat ? Html.Of($"<td class=\"center\">{(hazmatClass is null ? "" : "X")}</td>") : Html.Empty)}
                      <td>{i + 1}</td>
                      <td><div>{description}</div>{(hazmatClass is null ? Html.Empty : Html.Of($"<div class=\"hazmat\">HAZMAT class {hazmatClass}</div>"))}{customsLine}</td>
                      <td>{partNumber}</td>
                      <td class="num">{line.Quantity}</td>
                      <td class="num">—</td>
                      <td>Carton</td>
                    </tr>
                    """);
            });

        return Html.Of($"""
            <table class="lines articles">
              <colgroup>{(hasHazmat ? Html.Of($"<col style=\"width:28pt\">") : Html.Empty)}<col style="width:35pt"><col><col style="width:90pt"><col style="width:55pt"><col style="width:55pt"><col style="width:80pt"></colgroup>
              <thead><tr>{(hasHazmat ? Html.Of($"<th>HM</th>") : Html.Empty)}<th>#</th><th>DESCRIPTION OF ARTICLES</th><th>PART #</th><th class="num">QTY</th><th class="num">WEIGHT</th><th>PACKAGING</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html Packages(Shipment shipment)
    {
        if (shipment.Packages.Count == 0)
            return Html.Empty;

        var rows = shipment.Packages.ToList().Select((pkg, i) =>
        {
            var dims = pkg.Length.HasValue && pkg.Width.HasValue && pkg.Height.HasValue
                ? $"{pkg.Length:G} × {pkg.Width:G} × {pkg.Height:G} in"
                : "—";
            var weight = pkg.Weight.HasValue ? $"{pkg.Weight.Value:F1} lbs" : "—";
            return Html.Of($"<tr><td>{i + 1}</td><td>{pkg.TrackingNumber ?? "—"}</td><td class=\"num\">{weight}</td><td>{dims}</td></tr>");
        });

        return Html.Of($"""
            <div class="packages-title">PACKAGES</div>
            <table class="lines packages">
              <colgroup><col style="width:35pt"><col><col style="width:70pt"><col style="width:120pt"></colgroup>
              <thead><tr><th>#</th><th>TRACKING</th><th class="num">WEIGHT</th><th>DIMENSIONS</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html TotalsBox(Shipment shipment)
    {
        var totalWeight = shipment.Weight ?? shipment.Packages.Where(p => p.Weight.HasValue).Sum(p => p.Weight!.Value);
        var packages = shipment.Packages.Count > 0 ? shipment.Packages.Count : shipment.Lines.Count;
        return Html.Of($"""
            <div class="box totals-box fields">
              <div><div class="field-label">TOTAL PIECES:</div><div>{shipment.Lines.Sum(l => l.Quantity)}</div></div>
              <div><div class="field-label">TOTAL PACKAGES:</div><div>{packages}</div></div>
              <div><div class="field-label">TOTAL WEIGHT:</div><div>{(totalWeight > 0 ? $"{totalWeight:F1} lbs" : "—")}</div></div>
            </div>
            """);
    }

    private static Html Instructions(Shipment shipment)
        => string.IsNullOrWhiteSpace(shipment.Notes)
            ? Html.Empty
            : Html.Of($"<div class=\"instructions\"><div class=\"field-label\">SPECIAL INSTRUCTIONS:</div><div class=\"body\">{shipment.Notes}</div></div>");

    private static Html Signatures()
        => Html.Of($"""
            <div class="signatures first-row">
              {Signature("SHIPPER SIGNATURE:", "Signature / Date")}
              {Signature("CARRIER SIGNATURE:", "Signature / Date")}
            </div>
            <div class="signatures second-row">
              {Signature("RECEIVED BY:", "Print Name / Date")}
              {Signature("RECEIVER SIGNATURE:", "Signature / Date")}
            </div>
            """);

    private static Html Signature(string caption, string hint)
        => Html.Of($"<div class=\"sig\"><div class=\"caption\">{caption}</div><div class=\"line\">{hint}</div></div>");
}
