using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Inventory;

/// <summary>
/// Stock received while its receiving inspection is still open is stocked on QcHold, so it cannot be issued,
/// shipped, moved or planned against before anyone inspects it. Recording the inspection moves the accepted
/// quantity to Stored and waiving it moves the whole receipt; rejected quantity stays held. Nothing is moved
/// while an NCR or an active recall still holds the lot, and a lot hold release leaves held the quantity that
/// receipts still awaiting inspection put in each bin.
/// </summary>
public static class ReceivingStockHold
{
    public static BinContentStatus StockStatus(ReceivingInspectionStatus inspectionStatus)
        => IsOpen(inspectionStatus) ? BinContentStatus.QcHold : BinContentStatus.Stored;

    public static bool IsOpen(ReceivingInspectionStatus inspectionStatus)
        => inspectionStatus is ReceivingInspectionStatus.Pending or ReceivingInspectionStatus.InProgress;

    public static decimal BaseQuantity(PurchaseOrderLine line, decimal quantity)
        => line.PurchaseUnit?.ContentQuantity is decimal content and > 0 ? quantity * content : quantity;

    /// <summary>
    /// Moves up to <paramref name="quantity"/> (in the line's order unit) of the receipt's held stock in its bin
    /// to Stored and records a QC release movement. Returns the base quantity released.
    /// </summary>
    public static async Task<decimal> ReleaseReceiptAsync(
        AppDbContext db, ReceivingRecord record, decimal quantity, int userId, DateTimeOffset now, CancellationToken ct)
    {
        var line = record.PurchaseOrderLine;
        if (quantity <= 0 || line.PartId is not int partId || record.StorageLocationId is not int locationId)
            return 0;

        if (record.LotNumber is string lot
            && (await db.NcrsHoldingLot(partId, lot).AnyAsync(ct) || await db.IsLotUnderActiveRecallAsync(lot, ct)))
            return 0;

        var held = (await ActiveRowsAsync(db, partId, locationId, record.LotNumber, ct))
            .FirstOrDefault(bc => bc.Status == BinContentStatus.QcHold && bc.Quantity > 0);
        if (held is null)
            return 0;

        var released = Math.Min(held.Quantity, BaseQuantity(line, quantity));
        await MoveToStoredAsync(db, held, released, ct);

        db.BinMovements.Add(new BinMovement
        {
            EntityType = "part",
            EntityId = partId,
            Quantity = released,
            LotNumber = record.LotNumber,
            FromLocationId = locationId,
            ToLocationId = locationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.QcRelease,
        });
        return released;
    }

    /// <summary>
    /// Moves <paramref name="quantity"/> of a held row to Stored. A row released whole changes status; a partial
    /// release goes onto the Stored row for the same part, bin and lot, created when there is none.
    /// </summary>
    public static async Task MoveToStoredAsync(AppDbContext db, BinContent held, decimal quantity, CancellationToken ct)
    {
        if (quantity <= 0)
            return;

        if (quantity >= held.Quantity)
        {
            held.Status = BinContentStatus.Stored;
            return;
        }

        var stored = (await ActiveRowsAsync(db, held.EntityId, held.LocationId, held.LotNumber, ct))
            .FirstOrDefault(bc => !ReferenceEquals(bc, held) && bc.Status == BinContentStatus.Stored);
        if (stored is null)
        {
            stored = new BinContent
            {
                LocationId = held.LocationId,
                EntityType = held.EntityType,
                EntityId = held.EntityId,
                LotNumber = held.LotNumber,
                JobId = held.JobId,
                UomId = held.UomId,
                Status = BinContentStatus.Stored,
                PlacedBy = held.PlacedBy,
                PlacedAt = held.PlacedAt,
            };
            db.BinContents.Add(stored);
        }

        stored.Quantity += quantity;
        held.Quantity -= quantity;
    }

    /// <summary>
    /// The base quantity per bin that receipts of the part and lot still awaiting inspection put there — the
    /// part of a held lot a quality-hold release must leave held.
    /// </summary>
    public static async Task<Dictionary<int, decimal>> OpenInspectionQuantityByBinAsync(
        AppDbContext db, int partId, string? lotNumber, CancellationToken ct)
    {
        var open = await db.ReceivingRecords
            .Where(r => r.PurchaseOrderLine.PartId == partId
                && r.LotNumber == lotNumber
                && r.StorageLocationId != null
                && (r.InspectionStatus == ReceivingInspectionStatus.Pending
                    || r.InspectionStatus == ReceivingInspectionStatus.InProgress))
            .Select(r => new
            {
                LocationId = r.StorageLocationId!.Value,
                r.QuantityReceived,
                ContentQuantity = r.PurchaseOrderLine.PurchaseUnit != null
                    ? (decimal?)r.PurchaseOrderLine.PurchaseUnit.ContentQuantity
                    : null,
            })
            .ToListAsync(ct);

        return open
            .GroupBy(r => r.LocationId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(r => r.ContentQuantity is decimal content and > 0 ? r.QuantityReceived * content : r.QuantityReceived));
    }

    /// <summary>
    /// Moves the given held rows to Stored, leaving held in each bin the quantity that receipts still awaiting
    /// inspection put there. Returns the quantity released.
    /// </summary>
    public static async Task<decimal> ReleaseHeldRowsAsync(
        AppDbContext db, int partId, string? lotNumber, IReadOnlyList<BinContent> heldRows, CancellationToken ct)
    {
        var keepByBin = await OpenInspectionQuantityByBinAsync(db, partId, lotNumber, ct);
        var released = 0m;
        foreach (var row in heldRows.OrderBy(r => r.Id))
        {
            var keep = keepByBin.GetValueOrDefault(row.LocationId);
            var kept = Math.Min(keep, row.Quantity);
            keepByBin[row.LocationId] = keep - kept;

            var release = row.Quantity - kept;
            if (release <= 0)
                continue;
            await MoveToStoredAsync(db, row, release, ct);
            released += release;
        }
        return released;
    }

    private static async Task<List<BinContent>> ActiveRowsAsync(
        AppDbContext db, int partId, int locationId, string? lotNumber, CancellationToken ct)
    {
        await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == partId
                && bc.LocationId == locationId
                && bc.LotNumber == lotNumber
                && bc.RemovedAt == null)
            .LoadAsync(ct);

        return db.BinContents.Local
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == partId
                && bc.LocationId == locationId
                && bc.LotNumber == lotNumber
                && bc.RemovedAt == null)
            .OrderBy(bc => bc.PlacedAt)
            .ThenBy(bc => bc.Id)
            .ToList();
    }
}
