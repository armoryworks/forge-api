using MediatR;
using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record GenerateShipDocumentPdfQuery(int Id) : IRequest<byte[]>;

/// <summary>
/// Builds the combined landscape ship document for a shipment: the carrier label (stashed at label
/// creation) on the left + company details + Forge QR + carrier badge on the right.
/// </summary>
public class GenerateShipDocumentPdfHandler(
    AppDbContext db,
    IStorageService storage,
    ISystemSettingRepository settings) : IRequestHandler<GenerateShipDocumentPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateShipDocumentPdfQuery request, CancellationToken ct)
    {
        var shipment = await db.Shipments
            .Include(s => s.SalesOrder).ThenInclude(so => so.Customer)
            .Include(s => s.ShippingAddress)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        // The raw carrier label PNG, stashed in object storage when the label was created.
        byte[] labelPng;
        try
        {
            await using var stream = await storage.DownloadAsync(ShipLabelStorage.Bucket, ShipLabelStorage.Key(request.Id), ct);
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

        // Carrier identity for the badge — prefer the assigned Carrier's service id, else the label name.
        string? carrierKey = shipment.Carrier;
        if (shipment.CarrierId is int cid)
        {
            var carrier = await db.Carriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct);
            carrierKey = carrier?.IntegrationServiceId ?? carrier?.Name ?? shipment.Carrier;
        }

        var document = new ShipDocumentPdfDocument(
            shipment, labelPng, companyName, companyAddress, companyPhone, CarrierBadge.For(carrierKey));
        return document.GeneratePdf();
    }
}
