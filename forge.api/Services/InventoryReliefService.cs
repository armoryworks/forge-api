using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// BE-1 / F-030: relieves inventory for a confirmed shipment, idempotently, one BinMovement per
/// bin-content touched per shipment line. Each line carries an InventoryRelievedAt timestamp;
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

            await RelieveLineAsync(line, partId.Value, userId, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task RelieveLineAsync(ShipmentLine line, int partId, int userId, CancellationToken ct)
    {
        var remaining = line.Quantity;

        // FIFO: oldest placed bins first; only Stored, not yet removed
        var bins = await db.BinContents
            .Where(bc => bc.EntityType == "Part"
                      && bc.EntityId == partId
                      && bc.Status == BinContentStatus.Stored
                      && bc.RemovedAt == null
                      && bc.Quantity > 0)
            .OrderBy(bc => bc.PlacedAt)
            .ToListAsync(ct);

        var totalAvailable = bins.Sum(b => b.Quantity);
        if (totalAvailable < remaining)
            throw new InvalidOperationException(
                $"Insufficient stock for part {partId} on ShipmentLine {line.Id}. " +
                $"Required: {remaining}, Available: {totalAvailable} (INV-INV1a — never-negative guard).");

        foreach (var bin in bins)
        {
            if (remaining <= 0) break;

            var take = Math.Min(bin.Quantity, remaining);
            bin.Quantity -= take;
            remaining -= take;

            db.BinMovements.Add(new BinMovement
            {
                EntityType = "ShipmentLine",
                EntityId = line.Id,
                Quantity = -take,
                FromLocationId = bin.LocationId,
                Reason = BinMovementReason.Ship,
                MovedBy = userId,
                MovedAt = DateTimeOffset.UtcNow,
                LotNumber = bin.LotNumber,
            });

            logger.LogInformation(
                "Relieved {Take} of part {PartId} from bin {BinId} for ShipmentLine {LineId}",
                take, partId, bin.Id, line.Id);
        }

        line.InventoryRelievedAt = DateTimeOffset.UtcNow;
    }
}
