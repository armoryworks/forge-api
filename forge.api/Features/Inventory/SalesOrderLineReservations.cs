using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

/// <summary>
/// Which sales order lines stock can be set aside for: lines of a part on an order that is neither a draft,
/// shipped, completed nor cancelled, with quantity still to ship. A line can hold reservations up to that open
/// quantity.
/// </summary>
public static class SalesOrderLineReservations
{
    private static readonly SalesOrderStatus[] ClosedStatuses =
        [SalesOrderStatus.Draft, SalesOrderStatus.Shipped, SalesOrderStatus.Completed, SalesOrderStatus.Cancelled];

    public static IQueryable<SalesOrderLine> Reservable(AppDbContext db, int partId)
        => db.SalesOrderLines
            .Where(l => l.PartId == partId
                && !ClosedStatuses.Contains(l.SalesOrder.Status)
                && l.Quantity > l.ShippedQuantity);

    public static async Task<Dictionary<int, decimal>> ReservedByLineAsync(
        AppDbContext db, IReadOnlyCollection<int> lineIds, CancellationToken ct)
        => await db.Reservations
            .AsNoTracking()
            .Where(r => r.DeletedAt == null && r.SalesOrderLineId != null && lineIds.Contains(r.SalesOrderLineId.Value))
            .GroupBy(r => r.SalesOrderLineId!.Value)
            .Select(g => new { LineId = g.Key, Quantity = g.Sum(r => r.Quantity) })
            .ToDictionaryAsync(x => x.LineId, x => x.Quantity, ct);
}
