using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Data.Services;

public class AtpService(AppDbContext db, IClock clock) : IAtpService
{
    public async Task<AtpResult> CalculateAtpAsync(int partId, decimal quantity, CancellationToken ct = default)
    {
        var part = await db.Parts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == partId, ct)
            ?? throw new KeyNotFoundException($"Part {partId} not found");

        // On-hand: sum of all bin contents for this part
        var onHand = await db.BinContents
            .AsNoTracking()
            .Where(bc => bc.EntityType == "part" && bc.EntityId == partId && bc.Status == BinContentStatus.Stored)
            .SumAsync(bc => bc.Quantity, ct);

        // Allocated to orders: sum of unfulfilled SO line quantities
        var allocatedToOrders = await db.Set<Core.Entities.SalesOrderLine>()
            .AsNoTracking()
            .Include(l => l.SalesOrder)
            .Where(l => l.PartId == partId
                && l.SalesOrder.Status != SalesOrderStatus.Cancelled
                && l.SalesOrder.Status != SalesOrderStatus.Completed
                && l.ShippedQuantity < l.Quantity)
            .SumAsync(l => (decimal)(l.Quantity - l.ShippedQuantity), ct);

        // Scheduled receipts: open PO line quantities not yet received
        var scheduledReceipts = await db.PurchaseOrderLines
            .AsNoTracking()
            .Include(l => l.PurchaseOrder)
            .Where(l => l.PartId == partId
                && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled
                && l.PurchaseOrder.Status != PurchaseOrderStatus.Closed
                && l.ReceivedQuantity < l.OrderedQuantity)
            .SumAsync(l => (decimal)(l.OrderedQuantity - l.ReceivedQuantity), ct);

        var atp = onHand + scheduledReceipts - allocatedToOrders;
        var canFulfill = atp >= quantity;

        DateOnly? earliestDate = null;
        if (canFulfill)
        {
            earliestDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        }
        else
        {
            earliestDate = await GetEarliestAvailableDateAsync(partId, quantity, ct);
        }

        return new AtpResult
        {
            PartId = partId,
            PartNumber = part.PartNumber,
            RequestedQuantity = quantity,
            OnHand = onHand,
            AllocatedToOrders = allocatedToOrders,
            ScheduledReceipts = scheduledReceipts,
            AvailableToPromise = atp,
            EarliestAvailableDate = earliestDate,
            CanFulfill = canFulfill,
        };
    }

    public async Task<DateOnly?> GetEarliestAvailableDateAsync(int partId, decimal quantity, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var horizon = today.AddDays(180);

        var timeline = await GetAtpTimelineAsync(partId, today, horizon, ct);
        foreach (var bucket in timeline)
        {
            if (bucket.NetAvailable >= quantity)
                return bucket.Date;
        }

        return null;
    }

    public async Task<List<AtpBucket>> GetAtpTimelineAsync(int partId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        _ = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partId, ct)
            ?? throw new KeyNotFoundException($"Part {partId} not found");

        var fromOffset = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        // Current on-hand
        var onHand = await db.BinContents
            .AsNoTracking()
            .Where(bc => bc.EntityType == "part" && bc.EntityId == partId && bc.Status == BinContentStatus.Stored)
            .SumAsync(bc => bc.Quantity, ct);

        // Scheduled supply by date (PO expected delivery)
        var supplyByDate = await db.PurchaseOrderLines
            .AsNoTracking()
            .Include(l => l.PurchaseOrder)
            .Where(l => l.PartId == partId
                && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled
                && l.PurchaseOrder.Status != PurchaseOrderStatus.Closed
                && l.PurchaseOrder.ExpectedDeliveryDate != null
                && l.PurchaseOrder.ExpectedDeliveryDate >= fromOffset
                && l.PurchaseOrder.ExpectedDeliveryDate <= toOffset
                && l.ReceivedQuantity < l.OrderedQuantity)
            .GroupBy(l => l.PurchaseOrder.ExpectedDeliveryDate!.Value.Date)
            .Select(g => new { Date = g.Key, Quantity = g.Sum(l => (decimal)(l.OrderedQuantity - l.ReceivedQuantity)) })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.Date), x => x.Quantity, ct);

        // Demand by date (SO required/due dates — use SalesOrder's RequiredDate)
        var demandByDate = await db.Set<Core.Entities.SalesOrderLine>()
            .AsNoTracking()
            .Include(l => l.SalesOrder)
            .Where(l => l.PartId == partId
                && l.SalesOrder.Status != SalesOrderStatus.Cancelled
                && l.SalesOrder.Status != SalesOrderStatus.Completed
                && l.SalesOrder.RequestedDeliveryDate != null
                && l.SalesOrder.RequestedDeliveryDate >= fromOffset
                && l.SalesOrder.RequestedDeliveryDate <= toOffset
                && l.ShippedQuantity < l.Quantity)
            .GroupBy(l => l.SalesOrder.RequestedDeliveryDate!.Value.Date)
            .Select(g => new { Date = g.Key, Quantity = g.Sum(l => (decimal)(l.Quantity - l.ShippedQuantity)) })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.Date), x => x.Quantity, ct);

        // Build weekly buckets
        var buckets = new List<AtpBucket>();
        var cumulativeSupply = onHand;
        var cumulativeDemand = 0m;
        var current = from;

        while (current <= to)
        {
            var weekEnd = current.AddDays(6);

            // Sum supply for this week
            for (var d = current; d <= weekEnd && d <= to; d = d.AddDays(1))
            {
                if (supplyByDate.TryGetValue(d, out var supply))
                    cumulativeSupply += supply;
                if (demandByDate.TryGetValue(d, out var demand))
                    cumulativeDemand += demand;
            }

            buckets.Add(new AtpBucket
            {
                Date = current,
                CumulativeSupply = cumulativeSupply,
                CumulativeDemand = cumulativeDemand,
                NetAvailable = cumulativeSupply - cumulativeDemand,
            });

            current = weekEnd.AddDays(1);
        }

        return buckets;
    }
}
