using System.Globalization;

using Forge.Api.Services.Pdf;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.Quality.Coa;

/// <summary>
/// The certificate of analysis PDF: company header, the lot and part it certifies, the results table
/// (characteristic, specification, result, unit, method, pass/fail), the conformance statement and a
/// signature line. Built from the frozen certificate, so a reprint shows what was certified.
/// </summary>
public static class CertificateOfAnalysisPdfDocument
{
    private const string Styles = """
        .page-footer { color: #757575; }
        .head .coa-title { font-size: 18pt; font-weight: 700; color: #00796b; }
        .head .issued { font-size: 9pt; }
        .head .voided { font-weight: 700; color: #d32f2f; }
        .facts { display: flex; align-items: flex-start; }
        .facts > div { width: 50%; }
        .results { margin-top: 14pt; }
        table.lines.results thead tr { background: #00796b; }
        table.lines.results .verdict { font-weight: 600; }
        .pass { color: #388e3c; }
        .fail { color: #d32f2f; }
        .statement { margin-top: 14pt; font-weight: 600; }
        .statement.fail { color: #d32f2f; }
        .coa-notes { margin-top: 10pt; }
        .coa-notes .body { white-space: pre-wrap; }
        .grey { color: #757575; }
        .tiny { font-size: 8pt; }
        .signatures { display: flex; align-items: flex-start; margin-top: 30pt; break-inside: avoid; }
        .signatures .sign { flex: 1 1 auto; }
        .signatures .date { flex: 0 0 140pt; width: 140pt; margin-left: 30pt; }
        .signatures .line { border-top: 1pt solid #757575; margin-top: 24pt; }
        .generated-by { margin-top: 8pt; }
        """;

    public static HtmlPdfDocument Build(
        CertificateOfAnalysis certificate,
        string? companyName,
        string? generatedByName,
        CertificateOfAnalysisPdfLabels labels)
    {
        var body = Html.Of($"{Header(certificate, companyName, labels)}{Facts(certificate, labels)}{Results(certificate, labels)}{Closing(certificate, generatedByName, labels)}");
        return PdfHtml.Document(
            $"{labels.Title} {certificate.CertificateNumber}", body, certificate.CertificateNumber, styles: Styles);
    }

    private static string FormatDate(DateTimeOffset? value, CertificateOfAnalysisPdfLabels labels) =>
        value?.ToString(labels.DateFormat, CultureInfo.InvariantCulture) ?? "—";

    private static Html Header(CertificateOfAnalysis certificate, string? companyName, CertificateOfAnalysisPdfLabels labels)
    {
        var voided = certificate.Status == CertificateOfAnalysisStatus.Voided
            ? Html.Of($"<div class=\"voided\">{labels.Voided}</div>")
            : Html.Empty;

        return Html.Of($"""
            <div class="head">
              <div class="company">{companyName}</div>
              <div class="right">
                <div class="coa-title">{labels.Title}</div>
                <div class="number">#{certificate.CertificateNumber}</div>
                <div class="issued">{labels.Issued} {FormatDate(certificate.GeneratedAt, labels)}</div>
                {voided}
              </div>
            </div>
            <hr class="rule">
            """);
    }

    private static Html Fact(string label, string? value)
        => Html.Of($"<div><span class=\"strong\">{label} </span>{value}</div>");

    private static Html Facts(CertificateOfAnalysis certificate, CertificateOfAnalysisPdfLabels labels)
    {
        var lot = certificate.LotRecord;
        var part = certificate.Part;
        var revision = string.IsNullOrWhiteSpace(lot.PartRevision) ? part.Revision : lot.PartRevision;

        var left = new List<Html>
        {
            Fact(labels.Part, $"{part.PartNumber} — {part.Name}"),
            Fact(labels.Revision, revision),
            Fact(labels.Lot, lot.LotNumber),
            Fact(labels.Quantity, lot.Quantity.ToString("0.####", CultureInfo.InvariantCulture)),
        };
        if (!string.IsNullOrWhiteSpace(lot.SupplierLotNumber))
            left.Add(Fact(labels.SupplierLot, lot.SupplierLotNumber));

        var right = new List<Html> { Fact(labels.Manufactured, FormatDate(lot.CreatedAt, labels)) };
        if (lot.ExpirationDate is not null)
            right.Add(Fact(labels.Expires, FormatDate(lot.ExpirationDate, labels)));
        if (certificate.Customer is not null)
            right.Add(Fact(labels.Customer, certificate.Customer.GetDisplayName()));
        if (certificate.Shipment is not null)
            right.Add(Fact(labels.Shipment, certificate.Shipment.ShipmentNumber));

        return Html.Of($"<div class=\"facts\"><div>{left}</div><div>{right}</div></div>");
    }

    private static Html Results(CertificateOfAnalysis certificate, CertificateOfAnalysisPdfLabels labels)
    {
        var rows = certificate.Results
            .OrderBy(r => r.SortOrder)
            .Select(r => Html.Of($"""
                <tr>
                  <td>{r.Characteristic}</td>
                  <td>{r.Specification ?? "—"}</td>
                  <td>{r.Result ?? "—"}</td>
                  <td>{r.Unit ?? ""}</td>
                  <td>{r.Method ?? ""}</td>
                  <td class="verdict {(r.Pass ? "pass" : "fail")}">{(r.Pass ? labels.Pass : labels.Fail)}</td>
                </tr>
                """));

        return Html.Of($"""
            <table class="lines results">
              <colgroup><col style="width:124pt"><col style="width:82pt"><col style="width:124pt"><col style="width:40pt"><col style="width:82pt"><col style="width:60pt"></colgroup>
              <thead><tr><th>{labels.Characteristic}</th><th>{labels.Specification}</th><th>{labels.Result}</th><th>{labels.Unit}</th><th>{labels.Method}</th><th>{labels.Verdict}</th></tr></thead>
              <tbody>{rows}</tbody>
            </table>
            """);
    }

    private static Html Closing(CertificateOfAnalysis certificate, string? generatedByName, CertificateOfAnalysisPdfLabels labels)
    {
        var statement = certificate.Conforms
            ? Html.Of($"<div class=\"statement\">{labels.ConformsStatement}</div>")
            : Html.Of($"<div class=\"statement fail\">{labels.NonConformsStatement}</div>");

        var notes = string.IsNullOrWhiteSpace(certificate.Notes)
            ? Html.Empty
            : Html.Of($"<div class=\"coa-notes\"><div class=\"label\">{labels.Notes}</div><div class=\"body\">{certificate.Notes}</div></div>");

        var generatedBy = string.IsNullOrWhiteSpace(generatedByName)
            ? Html.Empty
            : Html.Of($"<div class=\"generated-by tiny grey\">{labels.GeneratedBy}: {generatedByName}</div>");

        return Html.Of($"""
            {statement}
            {notes}
            <div class="signatures">
              <div class="sign">
                <div class="small grey">{labels.AuthorizedBy}</div>
                <div class="line"></div>
                <div class="tiny grey">{labels.Signature}</div>
              </div>
              <div class="date">
                <div class="small">&nbsp;</div>
                <div class="line"></div>
                <div class="tiny grey">{labels.Date}</div>
              </div>
            </div>
            {generatedBy}
            """);
    }
}
