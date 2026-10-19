using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// BE-1 / F-030: relieves inventory for a confirmed shipment, idempotently, one BinMovement per
/// bin-content touched per shipment line. Each movement is part-keyed with a positive quantity out of
/// the bin's location (the house convention) and carries the shipment line in <c>ShipmentLineId</c>;
/// a bin drawn to zero is marked removed. Each line carries an InventoryRelievedAt timestamp;
/// re-entry on an already-relieved line is a no-op (idempotency guard).
///
/// Caller is responsible for loading the shipment with Lines + each line's SalesOrderLine
/// before invoking. The caller also determines the trigger point (ShipShipment vs PickConfirm)
/// — that ruling is pending domain specialist sign-off per the eng-lead's Wave-1 queue.
///
/// Stock-insufficient: throws InvalidOperationException — preserves the never-negative invariant
/// (INV-INV1a) rather than silently creating a deficit. Caller may choose to wrap in a try/catch
/// and soft-log if backorder-then-relieve is the intended flow.
/// </summary>
public class InventoryReliefService(AppDbContext db, ILogger<InventoryReliefService> logger)
{
    /// <summary>
    /// Relieves inventory for all unrelieved lines in <paramref name="shipment"/>.
    /// Idempotent: lines where <c>InventoryRelievedAt != null</c> are skipped.
    /// </summary>
    /// <param name="shipment">
    ///   Must have <c>Lines</c> loaded. Each line must have <c>SalesOrderLine</c> loaded
    ///   (for <c>PartId</c> when <c>ShipmentLine.PartId</c> is null — the current seed pattern).
    /// </param>
    /// <param name="userId">Stamped on every BinMovement as <c>MovedBy</c>.</param>
    public async Task RelieveShipmentAsync(Shipment shipment, int userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var line in shipment.Lines)
        {
            if (line.InventoryRelievedAt is not null)
            {
                logger.LogDebug(
                    "ShipmentLine {LineId} already relieved at {At} — skipping (idempotent)",
                    line.Id, line.InventoryRelievedAt);
                continue;
            }

            var partId = line.PartId ?? line.SalesOrderLine?.PartId;
            if (partId is null)
            {
                logger.LogWarning(
                    "ShipmentLine {LineId} has no PartId and no linked SalesOrderLine with PartId — skipping inventory relief for this line",
                    line.Id);
                continue;
            }

            await RelieveLineAsync(line, partId.Value, userId, now, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task RelieveLineAsync(
        ShipmentLine line, int partId, int userId, DateTimeOffset now, CancellationToken ct)
    {
        var remaining = line.Quantity;

        // FIFO: oldest placed bins first; only Stored, not yet removed
        var bins = await db.BinContents
            .Where(bc => bc.EntityType == "part"   // canonical BinContent entity type (matches receipt stock-in)
                      && bc.EntityId == partId
                      && bc.Status == BinContentStatus.Stored
                      && bc.RemovedAt == null
                      && bc.Quantity > 0)
            .OrderBy(bc => bc.PlacedAt)
            .ToListAsync(ct);

        var totalAvailable = bins.Sum(b => b.Quantity);
        if (totalAvailable < remaining)
        {
            var held = await db.BinContents
                .Where(bc => bc.EntityType == "part"
                          && bc.EntityId == partId
                          && bc.Status == BinContentStatus.QcHold
                          && bc.RemovedAt == null
                          && bc.Quantity > 0)
                .OrderBy(bc => bc.PlacedAt)
                .ToListAsync(ct);
            if (held.Count > 0 && totalAvailable + held.Sum(bc => bc.Quantity) >= remaining)
                throw await LotQualityHold.RefusalAsync(db, partId, held[0].LotNumber, ct);
        }

        if (totalAvailable < remaining)
            throw new InvalidOperationException(
                $"Insufficient stock for part {partId} on ShipmentLine {line.Id}. " +
                $"Required: {remaining}, Available: {totalAvailable} (INV-INV1a — never-negative guard).");

        var drawn = new List<BinContent>();
        foreach (var bin in bins)
        {
            if (remaining <= 0) break;

            var take = Math.Min(bin.Quantity, remaining);
            bin.Quantity -= take;
            remaining -= take;
            drawn.Add(bin);

            db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = partId,
                Quantity = take,
                FromLocationId = bin.LocationId,
                Reason = BinMovementReason.Ship,
                MovedBy = userId,
                MovedAt = now,
                LotNumber = bin.LotNumber,
                ShipmentLineId = line.Id,
            });

            logger.LogInformation(
                "Relieved {Take} of part {PartId} from bin {BinId} for ShipmentLine {LineId}",
                take, partId, bin.Id, line.Id);
        }

        // S-MV1 (second axis): release the SO-line reservation(s) this shipment fulfils — soft-delete
        // each reservation and decrement its bin's held quantity (mirrors ReleaseReservationHandler).
        // The soft-delete global query filter makes this idempotent on re-ship. Full release on ship;
        // proportional release for partial shipments is a documented follow-up.
        if (line.SalesOrderLineId is int soLineId)
        {
            var reservations = await db.Reservations
                .Include(r => r.BinContent)
                .Where(r => r.SalesOrderLineId == soLineId)
                .ToListAsync(ct);
            foreach (var res in reservations)
            {
                res.DeletedAt = now;
                res.BinContent.ReservedQuantity = Math.Max(0, res.BinContent.ReservedQuantity - res.Quantity);
            }
        }

        foreach (var bin in drawn.Where(b => b.Quantity == 0 && b.ReservedQuantity == 0))
        {
            bin.RemovedAt = now;
            bin.RemovedBy = userId;
        }

        line.InventoryRelievedAt = now;
    }
}
