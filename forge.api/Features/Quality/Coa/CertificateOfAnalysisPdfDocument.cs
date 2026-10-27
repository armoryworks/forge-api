using System.Globalization;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.Quality.Coa;

/// <summary>
/// The certificate of analysis PDF: company header, the lot and part it certifies, the results table
/// (characteristic, specification, result, unit, method, pass/fail), the conformance statement and a
/// signature line. Rendered from the frozen certificate, so a reprint shows what was certified.
/// </summary>
public class CertificateOfAnalysisPdfDocument(
    CertificateOfAnalysis certificate,
    string? companyName,
    string? generatedByName,
    CertificateOfAnalysisPdfLabels labels) : IDocument
{
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginHorizontal(50);
            page.MarginVertical(40);
            page.DefaultTextStyle(x => x.WithScriptFallbacks().FontSize(10));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().AlignCenter().Text(text =>
            {
                text.Span(labels.Page).FontSize(8).FontColor(Colors.Grey.Darken1);
                text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                text.Span(labels.Of).FontSize(8).FontColor(Colors.Grey.Darken1);
                text.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private string FormatDate(DateTimeOffset? value) =>
        value?.ToString(labels.DateFormat, CultureInfo.InvariantCulture) ?? "—";

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    if (companyName is not null)
                        left.Item().Text(companyName).Bold().FontSize(18);
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text(labels.Title).Bold().FontSize(18).FontColor(Colors.Teal.Darken2);
                    right.Item().AlignRight().Text($"#{certificate.CertificateNumber}").FontSize(12);
                    right.Item().AlignRight().Text($"{labels.Issued} {FormatDate(certificate.GeneratedAt)}").FontSize(9);
                    if (certificate.Status == CertificateOfAnalysisStatus.Voided)
                        right.Item().AlignRight().Text(labels.Voided).Bold().FontColor(Colors.Red.Darken2);
                });
            });

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            col.Item().PaddingTop(10);
        });
    }

    private void ComposeContent(IContainer container)
    {
        var lot = certificate.LotRecord;
        var part = certificate.Part;

        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(t => { t.Span($"{labels.Part} ").SemiBold(); t.Span($"{part.PartNumber} — {part.Name}"); });
                    var revision = string.IsNullOrWhiteSpace(lot.PartRevision) ? part.Revision : lot.PartRevision;
                    left.Item().Text(t => { t.Span($"{labels.Revision} ").SemiBold(); t.Span(revision); });
                    left.Item().Text(t => { t.Span($"{labels.Lot} ").SemiBold(); t.Span(lot.LotNumber); });
                    left.Item().Text(t => { t.Span($"{labels.Quantity} ").SemiBold(); t.Span(lot.Quantity.ToString("0.####", CultureInfo.InvariantCulture)); });
                    if (!string.IsNullOrWhiteSpace(lot.SupplierLotNumber))
                        left.Item().Text(t => { t.Span($"{labels.SupplierLot} ").SemiBold(); t.Span(lot.SupplierLotNumber); });
                });

                row.RelativeItem().Column(right =>
                {
                    right.Item().Text(t => { t.Span($"{labels.Manufactured} ").SemiBold(); t.Span(FormatDate(lot.CreatedAt)); });
                    if (lot.ExpirationDate is not null)
                        right.Item().Text(t => { t.Span($"{labels.Expires} ").SemiBold(); t.Span(FormatDate(lot.ExpirationDate)); });
                    if (certificate.Customer is not null)
                        right.Item().Text(t => { t.Span($"{labels.Customer} ").SemiBold(); t.Span(certificate.Customer.GetDisplayName()); });
                    if (certificate.Shipment is not null)
                        right.Item().Text(t => { t.Span($"{labels.Shipment} ").SemiBold(); t.Span(certificate.Shipment.ShipmentNumber); });
                });
            });

            col.Item().PaddingTop(14).Element(ComposeResults);

            col.Item().PaddingTop(14).Text(certificate.Conforms ? labels.ConformsStatement : labels.NonConformsStatement)
                .SemiBold().FontColor(certificate.Conforms ? Colors.Black : Colors.Red.Darken2);

            if (!string.IsNullOrWhiteSpace(certificate.Notes))
            {
                col.Item().PaddingTop(10).Text(labels.Notes).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().Text(certificate.Notes);
            }

            col.Item().PaddingTop(30).Row(row =>
            {
                row.RelativeItem().Column(sig =>
                {
                    sig.Item().Text(labels.AuthorizedBy).FontSize(9).FontColor(Colors.Grey.Darken1);
                    sig.Item().PaddingTop(24).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                    sig.Item().Text(labels.Signature).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(30);
                row.ConstantItem(140).Column(date =>
                {
                    date.Item().Text(" ").FontSize(9);
                    date.Item().PaddingTop(24).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                    date.Item().Text(labels.Date).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });

            if (!string.IsNullOrWhiteSpace(generatedByName))
                col.Item().PaddingTop(8).Text($"{labels.GeneratedBy}: {generatedByName}").FontSize(8).FontColor(Colors.Grey.Darken1);
        });
    }

    private void ComposeResults(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(3);
                cols.RelativeColumn(2);
                cols.RelativeColumn(3);
                cols.ConstantColumn(40);
                cols.RelativeColumn(2);
                cols.ConstantColumn(60);
            });

            table.Header(header =>
            {
                var style = TextStyle.Default.SemiBold().FontSize(9).FontColor(Colors.White);
                foreach (var title in new[] { labels.Characteristic, labels.Specification, labels.Result, labels.Unit, labels.Method, labels.Verdict })
                    header.Cell().Background(Colors.Teal.Darken2).Padding(5).Text(title).Style(style);
            });

            var rows = certificate.Results.OrderBy(r => r.SortOrder).ToList();
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                table.Cell().Background(bg).Padding(5).Text(r.Characteristic);
                table.Cell().Background(bg).Padding(5).Text(r.Specification ?? "—");
                table.Cell().Background(bg).Padding(5).Text(r.Result ?? "—");
                table.Cell().Background(bg).Padding(5).Text(r.Unit ?? "");
                table.Cell().Background(bg).Padding(5).Text(r.Method ?? "");
                table.Cell().Background(bg).Padding(5).Text(r.Pass ? labels.Pass : labels.Fail)
                    .SemiBold().FontColor(r.Pass ? Colors.Green.Darken2 : Colors.Red.Darken2);
            }
        });
    }
}
