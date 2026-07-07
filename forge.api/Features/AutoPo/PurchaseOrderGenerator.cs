using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.AutoPo;

public class PurchaseOrderGenerator(
    AppDbContext db,
    IPurchaseOrderRepository poRepo,
    IBarcodeService barcodeService)
{
    public async Task<PurchaseOrder> GeneratePurchaseOrder(
        int vendorId,
        List<(int PartId, string Description, int Quantity, decimal UnitPrice, DateTimeOffset NeededBy)> lines,
        PurchaseOrderStatus status,
        string? notes,
        CancellationToken ct,
        string? originReference = null)
    {
        var poNumber = await poRepo.GenerateNextPONumberAsync(ct);

        var po = new PurchaseOrder
        {
            PONumber = poNumber,
            VendorId = vendorId,
            Status = status,
            ExpectedDeliveryDate = lines.Min(l => l.NeededBy),
            Notes = notes,
            // S4b provenance — this generator only runs from the demand-driven
            // AutoPurchaseOrderJob, so every PO it emits is MRP-originated.
            // OriginReference column is varchar(200); truncate defensively.
            OriginSource = PoOriginSource.AutoMrp,
            OriginReference = originReference is { Length: > 200 }
                ? originReference[..200]
                : originReference,
        };

        foreach (var line in lines)
        {
            po.Lines.Add(new PurchaseOrderLine
            {
                PartId = line.PartId,
                Description = line.Description,
                OrderedQuantity = line.Quantity,
                UnitPrice = line.UnitPrice,
            });
        }

        await poRepo.AddAsync(po, ct);
        await db.SaveChangesAsync(ct);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.PurchaseOrder, po.Id, po.PONumber, ct);

        return po;
    }
}
