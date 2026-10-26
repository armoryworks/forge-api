using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Forge.Api.Services;
using Forge.Core.Entities;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// The combined "wrapped" ship document: a landscape page with the carrier's shipping label on the
/// left, and the company details + Forge master QR (scan-to-ship) + a color-coded, symbolic carrier
/// badge on the right so the carrier is unmistakable at a glance. A cross-border shipment also lists
/// each line's HTS code and country of origin.
/// </summary>
public class ShipDocumentPdfDocument(
    Shipment shipment,
    byte[] labelPng,
    string companyName,
    string? companyAddress,
    string? companyPhone,
    CarrierBadgeStyle carrier,
    ShipmentCustoms? customs = null) : IDocument
{
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter.Landscape());
            page.Margin(26);
            page.DefaultTextStyle(x => x.WithScriptFallbacks().FontSize(11));

            page.Content().Row(row =>
            {
                // LEFT — the carrier shipping label, fit within a bordered frame.
                row.RelativeItem(5).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(12)
                    .AlignMiddle().AlignCenter()
                    .Image(labelPng).FitArea();

                row.ConstantItem(22);

                // RIGHT — carrier badge, company block, shipment summary, Forge QR.
                row.RelativeItem(4).Column(right =>
                {
                    // Carrier badge: brand-colored band with an accent stripe + the carrier symbol.
                    right.Item().Background(carrier.Primary).BorderLeft(10).BorderColor(carrier.Accent)
                        .Padding(14).Column(b =>
                        {
                            b.Item().Text(carrier.Label).Bold().FontSize(30).FontColor(Colors.White);
                            b.Item().Text("SHIP VIA").FontSize(9).FontColor(Colors.White).LetterSpacing(0.2f);
                        });

                    right.Item().PaddingTop(16).Text(companyName).Bold().FontSize(18);
                    if (!string.IsNullOrWhiteSpace(companyAddress))
                        right.Item().Text(companyAddress!).FontSize(11).FontColor(Colors.Grey.Darken2);
                    if (!string.IsNullOrWhiteSpace(companyPhone))
                        right.Item().Text(companyPhone!).FontSize(11).FontColor(Colors.Grey.Darken2);

                    right.Item().PaddingTop(14).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                    right.Item().PaddingTop(12).Text($"Shipment {shipment.ShipmentNumber}").Bold().FontSize(14);
                    right.Item().Text($"Order {shipment.SalesOrder.OrderNumber}").FontSize(11);
                    right.Item().Text($"Ship to: {shipment.SalesOrder.Customer.Name}").FontSize(11);
                    if (!string.IsNullOrWhiteSpace(shipment.TrackingNumber))
                        right.Item().Text($"Tracking: {shipment.TrackingNumber}").FontSize(11).FontColor(Colors.Grey.Darken2);

                    if (customs is not null)
                    {
                        right.Item().PaddingTop(10)
                            .Text($"Customs: {customs.ShipFromCountry} to {customs.ShipToCountry}").Bold().FontSize(10);
                        foreach (var line in shipment.Lines.OrderBy(l => l.Id))
                        {
                            var info = customs.For(line);
                            var label = line.Part?.PartNumber ?? line.SalesOrderLine?.Part?.PartNumber
                                ?? line.SalesOrderLine?.Description ?? line.Description ?? "—";
                            right.Item().Text(
                                    $"{label} × {line.Quantity} · HTS {ShipmentCustoms.Display(info.HtsCode)} · Origin {ShipmentCustoms.Display(info.CountryOfOrigin)}")
                                .FontSize(9);
                        }
                    }

                    // Forge master QR — the coverage-bound scan-to-ship code.
                    var master = shipment.ScanCode
                        ?? ShipmentScanCode.Compute(shipment.ShipmentNumber, shipment.Lines);
                    right.Item().PaddingTop(18).Row(qr =>
                    {
                        qr.ConstantItem(120).Column(q =>
                        {
                            q.Item().Width(112).Image(QrCodeRenderer.Png(master));
                            q.Item().AlignCenter().Text("SCAN TO SHIP").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                        qr.RelativeItem().AlignBottom().PaddingLeft(10)
                            .Text(companyName).FontSize(9).FontColor(Colors.Grey.Medium);
                    });
                });
            });
        });
    }
}
