using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Purchasing;

public static class RfqPdfDocument
{
    private const string Styles = """
        @page { margin: 36pt 40pt 50pt; }
        .issuer { padding-right: 20pt; }
        .issuer .name { font-size: 16pt; font-weight: 700; }
        .doc { width: 220pt; flex-shrink: 0; text-align: right; }
        .doc .title { font-size: 18pt; font-weight: 700; line-height: 1.1; }
        .doc .number { font-size: 12pt; font-weight: 700; }
        .heavy-rule { border: 0; border-top: 1.5pt solid #000; margin: 8pt 0; }
        .box { width: 260pt; border: 0.75pt solid #000; padding: 6pt; margin-bottom: 10pt; }
        .box .caption { font-size: 8pt; font-weight: 600; }
        .box .name { font-weight: 700; }
        .box .gap { padding-top: 3pt; }
        .after-head { margin-top: 10pt; }
        table.grid { width: 100%; border-collapse: collapse; font-size: 8pt; }
        table.grid thead { display: table-header-group; }
        table.grid tr { break-inside: avoid; }
        table.grid th { background: #eeeeee; border-bottom: 1pt solid #000; padding: 4pt 3pt; font-weight: 600; text-align: left; }
        table.grid td { border-bottom: 0.5pt solid #9e9e9e; padding: 4pt 3pt; vertical-align: top; }
        table.grid .num { text-align: right; white-space: nowrap; }
        .block { font-size: 9pt; break-inside: avoid; }
        .block .heading { font-weight: 600; }
        .section { margin-top: 12pt; }
        .section .body { padding-top: 2pt; white-space: pre-wrap; }
        .response { margin-top: 16pt; }
        """;

    public static HtmlPdfDocument Build(
        RequestForQuote rfq,
        Vendor? vendor,
        string? companyName,
        string? companyPhone,
        string? companyEmail,
        CompanyLocation? location)
    {
        var body = Html.Of($"""
            {Header(rfq, vendor, companyName, companyPhone, companyEmail, location)}
            {VendorBlock(vendor)}
            {Lines(rfq)}
            {Section("Description", rfq.Description)}
            {Section("Special Instructions", rfq.SpecialInstructions)}
            {Response(rfq)}
            """);
        return PdfHtml.Document($"Request for Quote {rfq.RfqNumber}", body, rfq.RfqNumber, styles: Styles, author: companyName);
    }

    private static string LineDescription(RequestForQuote rfq) =>
        string.IsNullOrWhiteSpace(rfq.Part.Description) ? rfq.Part.Name : rfq.Part.Description;

    private static string Uom(RequestForQuote rfq) => rfq.Part.StockUom?.Code ?? string.Empty;

    private static Html Header(
        RequestForQuote rfq, Vendor? vendor, string? companyName, string? companyPhone, string? companyEmail, CompanyLocation? location)
    {
        var phone = string.IsNullOrWhiteSpace(companyPhone) ? location?.Phone : companyPhone;
        var address = location is null
            ? Html.Empty
            : Html.Of($"""
                <div class="small">{location.Line1}</div>
                {Small(location.Line2)}
                <div class="small">{CityLine(location.City, location.State, location.PostalCode)}</div>
                """);
        var issued = rfq.SentAt ?? rfq.CreatedAt;

        return Html.Of($"""
            <div class="head">
              <div class="issuer">
                {(string.IsNullOrWhiteSpace(companyName) ? Html.Empty : Html.Of($"<div class=\"name\">{companyName}</div>"))}
                {address}
                {(string.IsNullOrWhiteSpace(phone) ? Html.Empty : Html.Of($"<div class=\"small\">Phone: {phone}</div>"))}
                {(string.IsNullOrWhiteSpace(companyEmail) ? Html.Empty : Html.Of($"<div class=\"small\">Email: {companyEmail}</div>"))}
              </div>
              <div class="doc">
                <div class="title">REQUEST FOR QUOTE</div>
                <div class="number">RFQ #: {rfq.RfqNumber}</div>
                <div>Date: {issued:MM/dd/yyyy}</div>
                {(rfq.ResponseDeadline.HasValue ? Html.Of($"<div class=\"strong\">Response Due: {rfq.ResponseDeadline.Value:MM/dd/yyyy}</div>") : Html.Empty)}
                <div>Required By: {rfq.RequiredDate:MM/dd/yyyy}</div>
                {(string.IsNullOrWhiteSpace(vendor?.VendorNumber) ? Html.Empty : Html.Of($"<div>Vendor #: {vendor.VendorNumber}</div>"))}
              </div>
            </div>
            <hr class="heavy-rule">
            """);
    }

    private static Html VendorBlock(Vendor? vendor)
    {
        if (vendor is null)
            return Html.Of($"<div class=\"after-head\"></div>");

        return Html.Of($"""
            <div class="box">
              <div class="caption">TO</div>
              <div class="name">{vendor.CompanyName}</div>
              {Small(vendor.Address)}
              {Small(CityLine(vendor.City, vendor.State, vendor.ZipCode))}
              {Small(vendor.Country)}
              {(string.IsNullOrWhiteSpace(vendor.ContactName) ? Html.Empty : Html.Of($"<div class=\"small gap\">Attn: {vendor.ContactName}</div>"))}
              {(string.IsNullOrWhiteSpace(vendor.Phone) ? Html.Empty : Html.Of($"<div class=\"small\">Phone: {vendor.Phone}</div>"))}
              {(string.IsNullOrWhiteSpace(vendor.Email) ? Html.Empty : Html.Of($"<div class=\"small\">Email: {vendor.Email}</div>"))}
            </div>
            """);
    }

    private static Html Lines(RequestForQuote rfq) => Html.Of($"""
        <table class="grid">
          <colgroup><col style="width:18pt"><col style="width:90pt"><col><col style="width:60pt"><col style="width:44pt"></colgroup>
          <thead><tr><th>#</th><th>Part #</th><th>Description</th><th class="num">Qty</th><th>UOM</th></tr></thead>
          <tbody>
            <tr>
              <td>1</td>
              <td>{rfq.Part.PartNumber}</td>
              <td>{LineDescription(rfq)}</td>
              <td class="num">{rfq.Quantity:#,##0.####}</td>
              <td>{Uom(rfq)}</td>
            </tr>
          </tbody>
        </table>
        """);

    private static Html Section(string title, string? body)
        => string.IsNullOrWhiteSpace(body)
            ? Html.Empty
            : Html.Of($"<div class=\"block section\"><div class=\"heading\">{title}</div><div class=\"body\">{body}</div></div>");

    private static Html Response(RequestForQuote rfq)
    {
        var due = rfq.ResponseDeadline.HasValue
            ? Html.Of($"<div>Responses are due by {rfq.ResponseDeadline.Value:MM/dd/yyyy}.</div>")
            : Html.Empty;

        return Html.Of($"""
            <div class="block response">
              <div class="heading">Response</div>
              <div>Please quote unit price, lead time, minimum order quantity and any tooling cost.</div>
              {due}
              <div>Please reference RFQ {rfq.RfqNumber} on your quotation.</div>
            </div>
            """);
    }

    private static Html Small(string? text)
        => string.IsNullOrWhiteSpace(text) ? Html.Empty : Html.Of($"<div class=\"small\">{text}</div>");

    private static string CityLine(string? city, string? state, string? postalCode)
    {
        var cityState = string.Join(", ", new[] { city, state }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.Join(" ", new[] { cityState, postalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
