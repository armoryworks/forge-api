using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

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
/// Every line draws only free stock: units reserved for other orders are left alone, while the line's own
/// sales-order reservations count as free to it and are drawn first. All lines are checked before any bin
/// changes, in line order, so two lines of one part share its stock. A stocked line without enough free
/// stock refuses the whole shipment with <see cref="InsufficientStockException"/>, naming each short line —
/// preserving the never-negative invariant (INV-INV1a) without shipping goods the books never relieve.
/// Stock on quality hold that would cover a short line throws <see cref="QualityHoldException"/> instead.
/// Lines with no part, or a service part that is never stocked, are skipped.
///
/// Inside a caller's transaction, each part's stock is read under an advisory lock taken in part-id order,
/// so two shipments of one part cannot both draw the same free units.
/// </summary>
public class InventoryReliefService(AppDbContext db, ILogger<InventoryReliefService> logger)
{
    /// <summary>
    /// Relieves inventory for all unrelieved lines in <paramref name="shipment"/>, or none of them.
    /// Idempotent: lines where <c>InventoryRelievedAt != null</c> are skipped.
    /// </summary>
    /// <param name="shipment">
    ///   Must have <c>Lines</c> loaded. Each line must have <c>SalesOrderLine</c> loaded
    ///   (for <c>PartId</c> when <c>ShipmentLine.PartId</c> is null — the current seed pattern).
    /// </param>
    /// <param name="userId">Stamped on every BinMovement as <c>MovedBy</c>.</param>
    /// <exception cref="InsufficientStockException">A stocked line lacks the free stock to relieve.</exception>
    public async Task RelieveShipmentAsync(Shipment shipment, int userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = new List<(ShipmentLine Line, int PartId)>();
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

            pending.Add((line, partId.Value));
        }

        var partIds = pending.Select(p => p.PartId).Distinct().Order().ToList();
        if (db.Database.CurrentTransaction is not null)
            foreach (var id in partIds)
                await db.AcquireAdvisoryLockAsync("inventory.ship-relief", id.ToString(CultureInfo.InvariantCulture), ct);

        var parts = await db.Parts.IgnoreQueryFilters().AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .Select(p => new { p.Id, p.PartNumber, p.InventoryClass })
            .ToDictionaryAsync(p => p.Id, ct);
        pending.RemoveAll(p => parts.TryGetValue(p.PartId, out var part) && part.InventoryClass == InventoryClass.Service);

        var stock = new StockState();
        var plans = new List<LinePlan>();
        var shortLines = new List<ShortStockLine>();
        foreach (var (line, partId) in pending)
        {
            var plan = await PlanLineAsync(line, partId, stock, ct);
            if (plan.Draws is null)
                shortLines.Add(new ShortStockLine(
                    line.Id, partId, parts.TryGetValue(partId, out var part) ? part.PartNumber : $"#{partId}",
                    line.Quantity, plan.Free));
            else
                plans.Add(plan);
        }

        if (shortLines.Count > 0)
        {
            logger.LogWarning(
                "Shipment {ShipmentId} refused: {Count} line(s) short of free stock", shipment.Id, shortLines.Count);
            throw new InsufficientStockException(ShortStockMessage(shortLines), shortLines);
        }

        foreach (var plan in plans)
            ApplyPlan(plan, userId, now);

        await db.SaveChangesAsync(ct);
    }

    private static string ShortStockMessage(IReadOnlyList<ShortStockLine> lines)
    {
        static string Qty(decimal q) => q.ToString("0.####", CultureInfo.InvariantCulture);
        var detail = string.Join("; ", lines.Select(l => $"{l.PartNumber} needs {Qty(l.Needed)}, {Qty(l.Free)} free"));
        return $"Not enough free stock to ship: {detail}. Receive or produce the stock, or take these lines off the shipment.";
    }

    private async Task<LinePlan> PlanLineAsync(ShipmentLine line, int partId, StockState stock, CancellationToken ct)
    {
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
                .Where(r => r.DeletedAt is null && !stock.ReleasedReservations.Contains(r.Id))
                .ToList()
            : [];
        var ownReserved = ownReservations
            .GroupBy(r => r.BinContentId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        decimal Takeable(BinContent bin)
        {
            var quantity = stock.Quantity(bin);
            return Math.Min(quantity, quantity - stock.Reserved(bin) + ownReserved.GetValueOrDefault(bin.Id));
        }
        bins = bins.OrderBy(b => !ownReserved.ContainsKey(b.Id)).ToList();

        var remaining = line.Quantity;
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
            return new LinePlan(line, partId, null, ownReservations, totalAvailable);
        }

        var draws = new List<(BinContent Bin, decimal Take)>();
        foreach (var bin in bins)
        {
            if (remaining <= 0) break;

            var take = Math.Min(Takeable(bin), remaining);
            if (take <= 0) continue;
            remaining -= take;
            draws.Add((bin, take));
        }

        foreach (var (bin, take) in draws)
            stock.Draw(bin, take);
        foreach (var res in ownReservations)
            stock.Release(res);

        return new LinePlan(line, partId, draws, ownReservations, totalAvailable);
    }

    private void ApplyPlan(LinePlan plan, int userId, DateTimeOffset now)
    {
        var line = plan.Line;
        foreach (var (bin, take) in plan.Draws!)
        {
            bin.Quantity -= take;

            db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = plan.PartId,
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
                take, plan.PartId, bin.Id, line.Id);
        }

        // S-MV1 (second axis): release the SO-line reservation(s) this shipment fulfils — soft-delete
        // each reservation and decrement its bin's held quantity (mirrors ReleaseReservationHandler).
        // The soft-delete global query filter makes this idempotent on re-ship. Full release on ship;
        // proportional release for partial shipments is a documented follow-up.
        foreach (var res in plan.OwnReservations)
        {
            res.DeletedAt = now;
            res.BinContent.ReservedQuantity = Math.Max(0, res.BinContent.ReservedQuantity - res.Quantity);
        }

        foreach (var bin in plan.Draws.Select(d => d.Bin).Where(b => b.Quantity == 0 && b.ReservedQuantity == 0))
        {
            bin.RemovedAt = now;
            bin.RemovedBy = userId;
        }

        line.InventoryRelievedAt = now;
    }

    private sealed record LinePlan(
        ShipmentLine Line,
        int PartId,
        List<(BinContent Bin, decimal Take)>? Draws,
        List<Reservation> OwnReservations,
        decimal Free);

    private sealed class StockState
    {
        private readonly Dictionary<int, decimal> _drawn = [];
        private readonly Dictionary<int, decimal> _released = [];

        public HashSet<int> ReleasedReservations { get; } = [];

        public decimal Quantity(BinContent bin) => bin.Quantity - _drawn.GetValueOrDefault(bin.Id);

        public decimal Reserved(BinContent bin) => Math.Max(0, bin.ReservedQuantity - _released.GetValueOrDefault(bin.Id));

        public void Draw(BinContent bin, decimal take) => _drawn[bin.Id] = _drawn.GetValueOrDefault(bin.Id) + take;

        public void Release(Reservation reservation)
        {
            ReleasedReservations.Add(reservation.Id);
            _released[reservation.BinContentId] = _released.GetValueOrDefault(reservation.BinContentId) + reservation.Quantity;
        }
    }
}
