using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Api.Features.Documents;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// Builds the wrapped ship-document PDF for a shipment and the set of entities it should be linked to
/// (Shipment, Sales Order, Job(s), Invoice). Shared by the get + regenerate handlers.
/// </summary>
public class ShipDocumentComposer(AppDbContext db, IStorageService storage, ISystemSettingRepository settings)
{
    public const string Kind = "ship-label";

    public record Composed(byte[] Pdf, string FileName, DocumentLinkTarget Primary, IReadOnlyCollection<DocumentLinkTarget> Links);

    public async Task<Composed> ComposeAsync(int shipmentId, CancellationToken ct)
    {
        var shipment = await db.Shipments
            .Include(s => s.SalesOrder).ThenInclude(so => so.Customer)
            .Include(s => s.ShippingAddress)
            .Include(s => s.Invoice)
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine!).ThenInclude(sol => sol.Jobs)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, ct)
            ?? throw new KeyNotFoundException($"Shipment {shipmentId} not found");

        // Raw carrier label PNG (stashed at label creation).
        byte[] labelPng;
        try
        {
            await using var stream = await storage.DownloadAsync(ShipLabelStorage.Bucket, ShipLabelStorage.Key(shipmentId), ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            labelPng = ms.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "No carrier label is stored for this shipment yet — create the shipping label first.");
        }

        var companyName = (await settings.FindByKeyAsync("company_name", ct))?.Value ?? "Forge";
        var companyAddress = (await settings.FindByKeyAsync("company_address", ct))?.Value;
        var companyPhone = (await settings.FindByKeyAsync("company_phone", ct))?.Value;

        string? carrierKey = shipment.Carrier;
        if (shipment.CarrierId is int cid)
        {
            var carrier = await db.Carriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct);
            carrierKey = carrier?.IntegrationServiceId ?? carrier?.Name ?? shipment.Carrier;
        }

        var pdf = new ShipDocumentPdfDocument(
            shipment, labelPng, companyName, companyAddress, companyPhone, CarrierBadge.For(carrierKey)).GeneratePdf();

        // Link the document to the shipment + its sales order + any jobs + the invoice (when present).
        var links = new List<DocumentLinkTarget> { new("SalesOrder", shipment.SalesOrderId) };
        if (shipment.Invoice is not null) links.Add(new DocumentLinkTarget("Invoice", shipment.Invoice.Id));
        foreach (var jobId in shipment.Lines
                     .SelectMany(l => l.SalesOrderLine?.Jobs ?? Enumerable.Empty<Core.Entities.Job>())
                     .Select(j => j.Id).Distinct())
            links.Add(new DocumentLinkTarget("Job", jobId));

        return new Composed(pdf, $"ship-document-{shipment.ShipmentNumber}.pdf",
            new DocumentLinkTarget("Shipment", shipmentId), links);
    }
}
