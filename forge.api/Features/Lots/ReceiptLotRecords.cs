using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Lots;

/// <summary>
/// Gives a purchased lot its lot record when a PO receipt names one, so lot trace and recall can start from
/// it. Only lot-tracked parts get one, and only while CAP-INV-LOTS is on. A later receipt of the same part and
/// lot adds to that record. Lot numbers are unique across parts, so when the number already belongs to another
/// part (vendors reuse lot strings) the receipt still goes through, no record is made, and the PO activity
/// says so.
/// </summary>
public static class ReceiptLotRecords
{
    public const string Capability = "CAP-INV-LOTS";

    public static async Task EnsureAsync(
        AppDbContext db,
        IBarcodeService? barcodes,
        ICapabilitySnapshotProvider? capabilities,
        PurchaseOrderLine line,
        string? lotNumber,
        decimal baseQuantity,
        CancellationToken ct)
    {
        if (lotNumber is null || line.Part is not { TraceabilityType: TraceabilityType.Lot } part
            || capabilities?.IsEnabled(Capability) != true)
            return;

        var existing = await db.LotRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.LotNumber == lotNumber, ct);

        if (existing is not null)
        {
            if (existing.PartId == part.Id && existing.DeletedAt is null)
            {
                existing.Quantity += baseQuantity;
                await db.SaveChangesAsync(ct);
                return;
            }

            db.LogActivityAt(
                "lot-record-skipped",
                existing.PartId == part.Id
                    ? $"Lot {lotNumber} was received on {part.PartNumber}, but its lot record was deleted, so no lot record was added"
                    : $"Lot {lotNumber} was received on {part.PartNumber}, but that lot number already belongs to another part, so no lot record was added",
                ("PurchaseOrder", line.PurchaseOrderId));
            await db.SaveChangesAsync(ct);
            return;
        }

        var lot = new LotRecord
        {
            LotNumber = lotNumber,
            PartId = part.Id,
            PartRevision = part.Revision,
            PurchaseOrderLineId = line.Id,
            Quantity = baseQuantity,
        };
        db.LotRecords.Add(lot);
        await db.SaveChangesAsync(ct);

        if (barcodes is not null)
            await barcodes.CreateBarcodeAsync(BarcodeEntityType.Lot, lot.Id, lot.LotNumber, ct);
    }
}
