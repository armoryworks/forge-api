using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// Hard-reserves goods bought back-to-back to the sales order line they were bought for. A receipt reserves no
/// more than the line still needs (ordered − shipped − already reserved) and no more than the bin has free, so a
/// repeat call tops up rather than double-reserves. Stock on a quality hold is never reserved; it is linked once
/// released.
/// </summary>
public static class BackToBackReservations
{
    public static async Task<decimal> ReserveAsync(
        AppDbContext db, int salesOrderLineId, int partId, BinContent binContent, decimal quantity, CancellationToken ct)
    {
        if (quantity <= 0m || binContent.Status == BinContentStatus.QcHold)
            return 0m;

        var line = await db.SalesOrderLines.AsNoTracking()
            .Where(l => l.Id == salesOrderLineId)
            .Select(l => new { l.Id, l.SalesOrderId, l.Quantity, l.ShippedQuantity, OrderNumber = l.SalesOrder.OrderNumber, l.LineNumber })
            .FirstOrDefaultAsync(ct);
        if (line is null)
            return 0m;

        var reserved = await db.Reservations
            .Where(r => r.SalesOrderLineId == salesOrderLineId)
            .SumAsync(r => (decimal?)r.Quantity, ct) ?? 0m;
        reserved += db.ChangeTracker.Entries<Reservation>()
            .Where(e => e.State == EntityState.Added && e.Entity.SalesOrderLineId == salesOrderLineId)
            .Sum(e => e.Entity.Quantity);

        var needed = line.Quantity - line.ShippedQuantity - reserved;
        var free = binContent.Quantity - binContent.ReservedQuantity;
        var toReserve = Math.Min(quantity, Math.Min(needed, free));
        if (toReserve <= 0m)
            return 0m;

        db.Reservations.Add(new Reservation
        {
            PartId = partId,
            BinContent = binContent,
            SalesOrderLineId = salesOrderLineId,
            Quantity = toReserve,
            Notes = $"Back-to-back receipt for {line.OrderNumber} line {line.LineNumber}",
        });
        binContent.ReservedQuantity += toReserve;
        db.LogActivityAt(
            "back-to-back-reserved",
            $"Reserved {toReserve:0.####} received back-to-back to line {line.LineNumber}",
            ("SalesOrder", line.SalesOrderId));
        return toReserve;
    }
}
