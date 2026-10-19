using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// Sales-order fulfilment against its shipments. A line's <see cref="SalesOrderLine.ShippedQuantity"/> counts
/// what has actually left (shipments Shipped, In transit or Delivered) and is set when a shipment ships. The
/// quantity still open for a new shipment also nets out shipments created but not shipped yet. Both read the
/// order's shipments, so the order must be loaded with its shipments and their lines.
/// </summary>
public static class SalesOrderShipping
{
    private static readonly ShipmentStatus[] ShippedStatuses =
        [ShipmentStatus.Shipped, ShipmentStatus.InTransit, ShipmentStatus.Delivered];

    private static readonly SalesOrderStatus[] FulfilmentStatuses =
    [
        SalesOrderStatus.Confirmed,
        SalesOrderStatus.InProduction,
        SalesOrderStatus.PartiallyShipped,
        SalesOrderStatus.Shipped,
    ];

    /// <summary>
    /// The line quantity not yet on any shipment that is still live (anything but Cancelled). A shipped
    /// quantity recorded beyond the line's shipments still counts as taken.
    /// </summary>
    public static decimal OpenToShip(SalesOrder order, SalesOrderLine line)
        => line.Quantity - Math.Max(line.ShippedQuantity, QuantityOn(order, line.Id, s => s.Status != ShipmentStatus.Cancelled));

    /// <summary>
    /// Recomputes each line's shipped quantity from the order's shipped shipments and moves the order to
    /// Shipped or Partially shipped to match. Orders that are draft, completed or cancelled keep their status.
    /// </summary>
    public static void RecordShipped(SalesOrder order)
    {
        foreach (var line in order.Lines)
            line.ShippedQuantity = QuantityOn(order, line.Id, s => ShippedStatuses.Contains(s.Status));

        if (!FulfilmentStatuses.Contains(order.Status))
            return;

        var goodsLines = order.Lines.Where(l => l.ShipsGoods).ToList();
        if (goodsLines.Count > 0 && goodsLines.All(l => l.IsFullyShipped))
            order.Status = SalesOrderStatus.Shipped;
        else if (order.Lines.Any(l => l.ShippedQuantity > 0))
            order.Status = SalesOrderStatus.PartiallyShipped;
    }

    private static decimal QuantityOn(SalesOrder order, int salesOrderLineId, Func<Shipment, bool> counts)
        => order.Shipments
            .Where(counts)
            .SelectMany(s => s.Lines)
            .Where(l => l.SalesOrderLineId == salesOrderLineId)
            .Sum(l => l.Quantity);
}
