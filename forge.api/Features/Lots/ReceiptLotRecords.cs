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
/// says so. While CAP-INV-LOTS is on, a lot-tracked part received without a lot gets a house lot number
/// (<see cref="LotNumbers"/>), so every receipt of it can be traced. The receipt's expiry date goes on the lot;
/// when none is given and the part has a shelf life, the lot expires that many days after the receipt. A later
/// receipt into the same lot keeps the earlier of the two expiry dates.
/// </summary>
public static class ReceiptLotRecords
{
    public const string Capability = "CAP-INV-LOTS";

    public static bool TracksLot(Part? part, ICapabilitySnapshotProvider? capabilities)
        => TracksLot(part?.TraceabilityType, capabilities);

    public static bool TracksLot(TraceabilityType? traceability, ICapabilitySnapshotProvider? capabilities)
        => traceability == TraceabilityType.Lot && capabilities?.IsEnabled(Capability) == true;

    public static async Task<string?> ResolveAsync(
        AppDbContext? db,
        IClock clock,
        Part? part,
        string? lotNumber,
        ICapabilitySnapshotProvider? capabilities,
        ICollection<string> reserved,
        CancellationToken ct)
    {
        if (lotNumber is not null || db is null || !TracksLot(part, capabilities))
            return lotNumber;
        return await LotNumbers.NextAsync(db, clock, ct, reserved);
    }

    public static async Task EnsureAsync(
        AppDbContext db,
        IBarcodeService? barcodes,
        ICapabilitySnapshotProvider? capabilities,
        PurchaseOrderLine line,
        string? lotNumber,
        decimal baseQuantity,
        DateTimeOffset? expirationDate,
        DateTimeOffset receivedAt,
        CancellationToken ct)
    {
        if (lotNumber is null || !TracksLot(line.Part, capabilities))
            return;
        var part = line.Part!;
        var expires = ExpirationFor(part, expirationDate, receivedAt);

        var existing = await db.LotRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.LotNumber == lotNumber, ct);

        if (existing is not null)
        {
            if (existing.PartId == part.Id && existing.DeletedAt is null)
            {
                existing.Quantity += baseQuantity;
                if (expires is not null && (existing.ExpirationDate is null || expires < existing.ExpirationDate))
                    existing.ExpirationDate = expires;
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
            ExpirationDate = expires,
        };
        db.LotRecords.Add(lot);
        await db.SaveChangesAsync(ct);

        if (barcodes is not null)
            await barcodes.CreateBarcodeAsync(BarcodeEntityType.Lot, lot.Id, lot.LotNumber, ct);
    }

    public static DateTimeOffset? ExpirationFor(Part part, DateTimeOffset? expirationDate, DateTimeOffset receivedAt)
    {
        if (expirationDate is not null)
            return expirationDate;
        return part.ShelfLifeDays is int days and > 0
            ? new DateTimeOffset(receivedAt.UtcDateTime.Date.AddDays(days), TimeSpan.Zero)
            : null;
    }
}
