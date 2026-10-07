using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Lots;

internal static class LotShipmentResolver
{
    private const string ShipmentLineEntityType = "ShipmentLine";

    public static Task<List<LotShipmentRow>> ForLotsAsync(
        AppDbContext db, IReadOnlyCollection<string> lotNumbers, CancellationToken ct)
    {
        if (lotNumbers.Count == 0)
            return Task.FromResult(new List<LotShipmentRow>());

        var movements = ShipMovements(db)
            .Where(m => m.LotNumber != null && lotNumbers.Contains(m.LotNumber));
        return ResolveAsync(db, movements, isApproximate: false, ct);
    }

    public static async Task<List<LotShipmentRow>> ForSalesOrderLinesAsync(
        AppDbContext db,
        IReadOnlyCollection<int> salesOrderLineIds,
        IReadOnlyCollection<string> exactLotNumbers,
        CancellationToken ct)
    {
        if (salesOrderLineIds.Count == 0)
            return [];

        var shipMovements = ShipMovements(db);
        var shipmentLineIds = await db.ShipmentLines
            .AsNoTracking()
            .Where(sl => sl.SalesOrderLineId != null && salesOrderLineIds.Contains(sl.SalesOrderLineId.Value)
                && !shipMovements.Any(m => m.EntityId == sl.Id
                    && m.LotNumber != null && exactLotNumbers.Contains(m.LotNumber)))
            .Select(sl => sl.Id)
            .ToListAsync(ct);
        if (shipmentLineIds.Count == 0)
            return [];

        var movements = shipMovements.Where(m => shipmentLineIds.Contains(m.EntityId));
        var relieved = await ResolveAsync(db, movements, isApproximate: true, ct);
        var unrelieved = await UnrelievedLinesAsync(db, shipmentLineIds, ct);
        return [.. relieved, .. unrelieved];
    }

    private static Task<List<LotShipmentRow>> UnrelievedLinesAsync(
        AppDbContext db, List<int> shipmentLineIds, CancellationToken ct)
    {
        var shipMovements = ShipMovements(db);
        return db.ShipmentLines
            .AsNoTracking()
            .Where(sl => shipmentLineIds.Contains(sl.Id) && sl.Quantity > 0
                && sl.Shipment.ShippedDate != null
                && sl.Shipment.Status != ShipmentStatus.Cancelled
                && !shipMovements.Any(m => m.EntityId == sl.Id))
            .Select(sl => new LotShipmentRow(
                sl.ShipmentId,
                sl.Shipment.ShipmentNumber,
                sl.Shipment.ShippedDate,
                sl.Shipment.TrackingNumber,
                sl.Shipment.SalesOrder.CustomerId,
                sl.Shipment.SalesOrder.Customer.Name,
                null,
                sl.Quantity,
                true))
            .ToListAsync(ct);
    }

    private static IQueryable<BinMovement> ShipMovements(AppDbContext db) =>
        db.BinMovements
            .AsNoTracking()
            .Where(m => m.Reason == BinMovementReason.Ship && m.EntityType == ShipmentLineEntityType);

    private static async Task<List<LotShipmentRow>> ResolveAsync(
        AppDbContext db, IQueryable<BinMovement> shipMovements, bool isApproximate, CancellationToken ct)
    {
        var movements = await shipMovements
            .Select(m => new { m.Id, ShipmentLineId = m.EntityId, m.LotNumber, m.Quantity })
            .ToListAsync(ct);
        if (movements.Count == 0)
            return [];

        var movementIds = movements.Select(m => m.Id).ToList();
        var reversedQuantities = (await db.BinMovements
                .AsNoTracking()
                .Where(m => m.ReversedMovementId != null && movementIds.Contains(m.ReversedMovementId.Value))
                .Select(m => new { OriginalId = m.ReversedMovementId!.Value, m.Quantity })
                .ToListAsync(ct))
            .GroupBy(r => r.OriginalId)
            .ToDictionary(g => g.Key, g => g.Sum(r => Math.Abs(r.Quantity)));

        var netByLineAndLot = movements
            .Select(m =>
            {
                var reversed = reversedQuantities.TryGetValue(m.Id, out var r) ? r : 0m;
                var shipped = -m.Quantity - Math.Sign(-m.Quantity) * reversed;
                return new { m.ShipmentLineId, m.LotNumber, Shipped = shipped };
            })
            .GroupBy(m => new { m.ShipmentLineId, m.LotNumber })
            .Select(g => new { g.Key.ShipmentLineId, g.Key.LotNumber, Shipped = g.Sum(x => x.Shipped) })
            .Where(x => x.Shipped > 0)
            .ToList();
        if (netByLineAndLot.Count == 0)
            return [];

        var lineIds = netByLineAndLot.Select(x => x.ShipmentLineId).Distinct().ToList();
        var lines = await db.ShipmentLines
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(sl => lineIds.Contains(sl.Id))
            .Select(sl => new
            {
                sl.Id,
                sl.ShipmentId,
                sl.Shipment.ShipmentNumber,
                sl.Shipment.ShippedDate,
                sl.Shipment.TrackingNumber,
                sl.Shipment.SalesOrder.CustomerId,
                CustomerName = sl.Shipment.SalesOrder.Customer.Name,
            })
            .ToDictionaryAsync(sl => sl.Id, ct);

        return netByLineAndLot
            .Where(x => lines.ContainsKey(x.ShipmentLineId))
            .Select(x => new { Line = lines[x.ShipmentLineId], x.LotNumber, x.Shipped })
            .GroupBy(x => new { x.Line.ShipmentId, x.LotNumber })
            .Select(g =>
            {
                var line = g.First().Line;
                return new LotShipmentRow(
                    line.ShipmentId,
                    line.ShipmentNumber,
                    line.ShippedDate,
                    line.TrackingNumber,
                    line.CustomerId,
                    line.CustomerName,
                    g.Key.LotNumber,
                    g.Sum(x => x.Shipped),
                    isApproximate);
            })
            .OrderBy(r => r.ShippedDate)
            .ThenBy(r => r.ShipmentNumber)
            .ToList();
    }
}
