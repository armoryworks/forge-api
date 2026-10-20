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
/// Each line is relieved on its own and draws only free stock: units reserved for other orders are left
/// alone, while the line's own sales-order reservations count as free to it and are drawn first. A line
/// without enough free stock is left unrelieved — preserving the never-negative invariant (INV-INV1a) —
/// and is recorded on the shipment's activity log, while the other lines are still relieved. Stock on
/// quality hold that would cover a short line throws <see cref="QualityHoldException"/>.
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
    /// <returns>The ids of the lines left unrelieved for want of free stock.</returns>
    public async Task<IReadOnlyList<int>> RelieveShipmentAsync(Shipment shipment, int userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var shortLines = new List<int>();
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

            if (await RelieveLineAsync(line, partId.Value, userId, now, ct) is decimal available)
            {
                shortLines.Add(line.Id);
                logger.LogWarning(
                    "ShipmentLine {LineId} not relieved: part {PartId} needs {Required}, {Available} free",
                    line.Id, partId, line.Quantity, available);
                db.ActivityLogs.Add(new ActivityLog
                {
                    EntityType = "Shipment",
                    EntityId = shipment.Id,
                    UserId = userId == 0 ? null : userId,
                    Action = "inventory_not_relieved",
                    Description = $"Inventory not relieved for shipment line {line.Id}: " +
                        $"{line.Quantity} needed, {available} free.",
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return shortLines;
    }

    private async Task<decimal?> RelieveLineAsync(
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

        var ownReservations = line.SalesOrderLineId is int ownLineId
            ? (await db.Reservations
                .Include(r => r.BinContent)
                .Where(r => r.SalesOrderLineId == ownLineId)
                .ToListAsync(ct))
                .Where(r => r.DeletedAt is null)
                .ToList()
            : [];
        var ownReserved = ownReservations
            .GroupBy(r => r.BinContentId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        decimal Takeable(BinContent bin)
            => Math.Min(bin.Quantity, bin.Quantity - bin.ReservedQuantity + ownReserved.GetValueOrDefault(bin.Id));
        bins = bins.OrderBy(b => !ownReserved.ContainsKey(b.Id)).ToList();

        var totalAvailable = bins.Sum(b => Math.Max(0m, Takeable(b)));
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
            return totalAvailable;
        }

        var drawn = new List<BinContent>();
        foreach (var bin in bins)
        {
            if (remaining <= 0) break;

            var take = Math.Min(Takeable(bin), remaining);
            if (take <= 0) continue;
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
        foreach (var res in ownReservations)
        {
            res.DeletedAt = now;
            res.BinContent.ReservedQuantity = Math.Max(0, res.BinContent.ReservedQuantity - res.Quantity);
        }

        foreach (var bin in drawn.Where(b => b.Quantity == 0 && b.ReservedQuantity == 0))
        {
            bin.RemovedAt = now;
            bin.RemovedBy = userId;
        }

        line.InventoryRelievedAt = now;
        return null;
    }
}
