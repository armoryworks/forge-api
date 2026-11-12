using System.Globalization;

using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.PurchaseOrders;

public static class PurchaseOrderPdfDocument
{
    private static readonly CultureInfo Format = CultureInfo.InvariantCulture;

    private const string Styles = """
        @page { margin: 36pt 40pt 50pt; }
        .watermark { position: fixed; top: 260pt; left: 0; width: 100%; text-align: center; transform: rotate(-35deg); font-size: 140pt; font-weight: 700; color: #eeeeee; z-index: -1; }
        .issuer { padding-right: 20pt; }
        .issuer .name { font-size: 16pt; font-weight: 700; }
        .doc { width: 200pt; flex-shrink: 0; text-align: right; }
        .doc .title { font-size: 20pt; font-weight: 700; line-height: 1.1; }
        .doc .number { font-size: 12pt; font-weight: 700; }
        .heavy-rule { border: 0; border-top: 1.5pt solid #000; margin: 8pt 0; }
        .boxes { display: flex; justify-content: space-between; align-items: stretch; margin-bottom: 10pt; }
        .box { width: 48%; border: 0.75pt solid #000; padding: 6pt; }
        .box .caption { font-size: 8pt; font-weight: 600; }
        .box .name { font-weight: 700; }
        .box .gap { padding-top: 3pt; }
        table.grid { width: 100%; border-collapse: collapse; font-size: 8pt; }
        table.grid thead { display: table-header-group; }
        table.grid tr { break-inside: avoid; }
        table.grid th { background: #eeeeee; border-bottom: 1pt solid #000; padding: 4pt 3pt; font-weight: 600; text-align: left; }
        table.grid td { border-bottom: 0.5pt solid #9e9e9e; padding: 4pt 3pt; vertical-align: top; }
        table.grid .num { text-align: right; white-space: nowrap; }
        table.grid .line-note { font-size: 7pt; font-style: italic; }
        .totals.sums { width: 230pt; margin-top: 8pt; }
        .totals.sums .row { padding: 2pt 0 0; }
        .totals.sums .grand { font-size: 11pt; }
        .totals.sums .after-rule { margin-top: 3pt; padding-top: 2pt; }
        .block { font-size: 9pt; break-inside: avoid; }
        .block .heading { font-weight: 600; }
        .terms { margin-top: 16pt; }
        .po-notes { margin-top: 12pt; }
        .po-notes .body { padding-top: 2pt; white-space: pre-wrap; }
        """;

    public static HtmlPdfDocument Build(
        PurchaseOrder po,
        IReadOnlyDictionary<int, string> vendorPartNumbers,
        string? companyName,
        string? companyPhone,
        string? companyEmail,
        CompanyLocation? shipTo,
        CompanyLocation? companyLocation = null)
    {
        companyLocation ??= shipTo;

        var watermark = IsDraft(po) ? Html.Of($"<div class=\"watermark\">DRAFT</div>") : Html.Empty;
        var body = Html.Of($"""
            {watermark}
            {Header(po, companyName, companyPhone, companyEmail, companyLocation)}
            {Parties(po, companyName, shipTo)}
            {Lines(po, vendorPartNumbers)}
            {Totals(po)}
            {Terms(po)}
            {Notes(po)}
            """);
        return PdfHtml.Document($"Purchase Order {po.PONumber}", body, po.PONumber, styles: Styles);
    }

    public static string UomFor(PurchaseOrderLine line) =>
        line.PurchaseUnit?.Label
        ?? line.Uom?.Code
        ?? line.Part?.StockUom?.Code
        ?? string.Empty;

    public static string Money(decimal amount, string currency) =>
        $"{amount.ToString("#,##0.00", Format)} {currency}";

    public static string UnitPrice(decimal amount) => amount.ToString("#,##0.00##", Format);

    private static bool IsDraft(PurchaseOrder po) => po.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval;

    private static IReadOnlyList<string> VendorAddressLines(PurchaseOrder po)
    {
        if (po.VendorAddress is { } address)
            return NonBlank(address.Line1, address.Line2, CityLine(address.City, address.State, address.PostalCode), address.Country);

        var vendor = po.Vendor;
        return NonBlank(vendor.Address, CityLine(vendor.City, vendor.State, vendor.ZipCode), vendor.Country);
    }

    private static IReadOnlyList<string> VendorContactLines(PurchaseOrder po)
    {
        var vendor = po.Vendor;
        var contact = po.VendorContact;
        var name = contact is null ? vendor.ContactName : PurchaseOrderParties.ContactName(contact);
        return NonBlank(
            Labelled("Attn", name),
            Labelled("Phone", FirstNonBlank(contact?.Phone, vendor.Phone)),
            Labelled("Fax", FirstNonBlank(contact?.Fax, vendor.Fax)),
            Labelled("Email", FirstNonBlank(contact?.Email, vendor.Email)));
    }

    private static string VendorPartNumberFor(PurchaseOrderLine line, IReadOnlyDictionary<int, string> vendorPartNumbers) =>
        line.PartId.HasValue && vendorPartNumbers.TryGetValue(line.PartId.Value, out var number)
            ? number
            : string.Empty;

    private static Html Header(
        PurchaseOrder po, string? companyName, string? companyPhone, string? companyEmail, CompanyLocation? location)
    {
        var phone = string.IsNullOrWhiteSpace(companyPhone) ? location?.Phone : companyPhone;
        var address = location is null
            ? Html.Empty
            : Html.Of($"""
                <div class="small">{location.Line1}</div>
                {Small(location.Line2)}
                <div class="small">{CityLine(location.City, location.State, location.PostalCode)}</div>
                """);
        var orderDate = po.SubmittedDate ?? po.CreatedAt;

        return Html.Of($"""
            <div class="head">
              <div class="issuer">
                {(string.IsNullOrWhiteSpace(companyName) ? Html.Empty : Html.Of($"<div class=\"name\">{companyName}</div>"))}
                {address}
                {(string.IsNullOrWhiteSpace(phone) ? Html.Empty : Html.Of($"<div class=\"small\">Phone: {phone}</div>"))}
                {(string.IsNullOrWhiteSpace(companyEmail) ? Html.Empty : Html.Of($"<div class=\"small\">Email: {companyEmail}</div>"))}
              </div>
              <div class="doc">
                <div class="title">PURCHASE ORDER</div>
                <div class="number">PO #: {po.PONumber}</div>
                <div>Date: {orderDate:MM/dd/yyyy}</div>
                {(po.ExpectedDeliveryDate.HasValue ? Html.Of($"<div>Expected Delivery: {po.ExpectedDeliveryDate.Value:MM/dd/yyyy}</div>") : Html.Empty)}
                {(string.IsNullOrWhiteSpace(po.Vendor.VendorNumber) ? Html.Empty : Html.Of($"<div>Vendor #: {po.Vendor.VendorNumber}</div>"))}
                {(po.IsBlanket ? Html.Of($"<div class=\"strong\">Blanket PO</div>") : Html.Empty)}
              </div>
            </div>
            <hr class="heavy-rule">
            """);
    }

    private static Html Parties(PurchaseOrder po, string? companyName, CompanyLocation? shipTo)
    {
        var contact = VendorContactLines(po)
            .Select((line, i) => Html.Of($"<div class=\"small{(i == 0 ? " gap" : string.Empty)}\">{line}</div>"));

        var shipToLines = shipTo is null
            ? Html.Empty
            : Html.Of($"""
                {(string.Equals(shipTo.Name, companyName, StringComparison.OrdinalIgnoreCase) ? Html.Empty : Html.Of($"<div class=\"small\">{shipTo.Name}</div>"))}
                <div class="small">{shipTo.Line1}</div>
                {Small(shipTo.Line2)}
                <div class="small">{CityLine(shipTo.City, shipTo.State, shipTo.PostalCode)}</div>
                {Small(shipTo.Country)}
                {(string.IsNullOrWhiteSpace(shipTo.Phone) ? Html.Empty : Html.Of($"<div class=\"small\">Phone: {shipTo.Phone}</div>"))}
                """);

        return Html.Of($"""
            <div class="boxes">
              <div class="box">
                <div class="caption">VENDOR</div>
                <div class="name">{po.Vendor.CompanyName}</div>
                {VendorAddressLines(po).Select(line => Html.Of($"<div class=\"small\">{line}</div>"))}
                {contact}
              </div>
              <div class="box">
                <div class="caption">SHIP TO</div>
                {(string.IsNullOrWhiteSpace(companyName) ? Html.Empty : Html.Of($"<div class=\"name\">{companyName}</div>"))}
                {shipToLines}
              </div>
            </div>
            """);
    }

    private static Html Lines(PurchaseOrder po, IReadOnlyDictionary<int, string> vendorPartNumbers)
    {
        var currency = po.QuoteCurrency;
        var dueDate = po.ExpectedDeliveryDate?.ToString("MM/dd/yyyy", Format) ?? string.Empty;

        var rows = po.Lines.OrderBy(l => l.Id).Select((line, i) =>
        {
            var description = string.IsNullOrWhiteSpace(line.Description)
                ? line.Part?.Description ?? string.Empty
                : line.Description;
            var note = string.IsNullOrWhiteSpace(line.Notes)
                ? Html.Empty
                : Html.Of($"<div class=\"line-note\">{line.Notes}</div>");

            return Html.Of($"""
                <tr>
                  <td>{i + 1}</td>
                  <td>{line.Part?.PartNumber ?? string.Empty}</td>
                  <td>{VendorPartNumberFor(line, vendorPartNumbers)}</td>
                  <td><div>{description}</div>{note}</td>
                  <td class="num">{line.OrderedQuantity:#,##0.####}</td>
                  <td>{UomFor(line)}</td>
                  <td class="num">{UnitPrice(line.UnitPrice)}</td>
                  <td>{dueDate}</td>
                  <td class="num">{Money(line.OrderedQuantity * line.UnitPrice, currency)}</td>
                </tr>
                """);
        });

        return Html.Of($"""
            <table class="grid">
              <colgroup><col style="width:18pt"><col style="width:62pt"><col style="width:62pt"><col><col style="width:40pt"><col style="width:38pt"><col style="width:50pt"><col style="width:56pt"><col style="width:66pt"></colgroup>
              <thead><tr><th>#</th><th>Part #</th><th>Vendor Part #</th><th>Description</th><th class="num">Qty</th><th>UOM</th><th class="num">Unit Price</th><th>Due</th><th class="num">Total</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html Totals(PurchaseOrder po)
    {
        var currency = po.QuoteCurrency;
        var subtotal = po.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice);

        return Html.Of($"""
            <div class="totals sums">
              {Row("Subtotal:", Money(subtotal, currency), "strong-label")}
              {(po.EstimatedFreight.HasValue ? Row("Estimated Freight:", Money(po.EstimatedFreight.Value, currency), "strong-label") : Html.Empty)}
              {Row("Total:", Money(subtotal + (po.EstimatedFreight ?? 0m), currency), "grand after-rule")}
            </div>
            """);
    }

    private static Html Terms(PurchaseOrder po)
    {
        var paymentTerms = string.IsNullOrWhiteSpace(po.Vendor.PaymentTerms)
            ? Html.Empty
            : Html.Of($"<div>Payment terms: {po.Vendor.PaymentTerms}</div>");
        var expires = po.IsBlanket && po.BlanketExpirationDate.HasValue
            ? Html.Of($"<div>Blanket order expires: {po.BlanketExpirationDate.Value:MM/dd/yyyy}</div>")
            : Html.Empty;

        return Html.Of($"""
            <div class="block terms">
              <div class="heading">Terms</div>
              {paymentTerms}
              <div>Shipping terms: {po.Incoterm.ToString().Replace('_', ' ')}</div>
              <div>Currency: {po.QuoteCurrency}</div>
              {expires}
              <div>Please reference PO {po.PONumber} on all packing slips and invoices.</div>
            </div>
            """);
    }

    private static Html Notes(PurchaseOrder po)
        => string.IsNullOrWhiteSpace(po.Notes)
            ? Html.Empty
            : Html.Of($"<div class=\"block po-notes\"><div class=\"heading\">Notes</div><div class=\"body\">{po.Notes}</div></div>");

    private static Html Row(string label, string amount, string style)
        => Html.Of($"<div class=\"row {style}\"><span>{label}</span><span>{amount}</span></div>");

    private static Html Small(string? text)
        => string.IsNullOrWhiteSpace(text) ? Html.Empty : Html.Of($"<div class=\"small\">{text}</div>");

    private static IReadOnlyList<string> NonBlank(params string?[] values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? Labelled(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{label}: {value}";

    private static string CityLine(string? city, string? state, string? postalCode)
    {
        var cityState = string.Join(", ", new[] { city, state }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.Join(" ", new[] { cityState, postalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
