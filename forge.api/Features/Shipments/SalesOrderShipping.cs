using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// Sales-order fulfilment against its shipments. A line's <see cref="SalesOrderLine.ShippedQuantity"/> counts
/// what has actually left (shipments Shipped, In transit or Delivered) and is set when a shipment ships. The
/// quantity still open for a new shipment also nets out shipments created but not shipped yet. Both read the
/// order's shipments, so the order must be loaded with its shipments and their lines. A line that ships inside its
/// under-ship tolerance, with nothing more on the way, closes the rest "within tolerance"
/// (<see cref="SalesOrderLine.ShortClosedQuantity"/>), so it stops counting as open demand or back order.
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

    public const string ToleranceKind = "sales-shipment";

    /// <summary>
    /// The line quantity not yet on any shipment that is still live (anything but Cancelled), less what was closed
    /// within tolerance. A shipped quantity recorded beyond the line's shipments still counts as taken.
    /// </summary>
    public static decimal OpenToShip(SalesOrder order, SalesOrderLine line)
        => line.Quantity - line.ShortClosedQuantity - Taken(order, line);

    /// <summary>The line quantity already shipped or on a live shipment — what a new shipment's over-ship check adds to.</summary>
    public static decimal Taken(SalesOrder order, SalesOrderLine line)
        => Math.Max(line.ShippedQuantity, QuantityOn(order, line.Id, s => s.Status != ShipmentStatus.Cancelled));

    /// <summary>
    /// Recomputes each line's shipped quantity from the order's shipped shipments, re-applies each line's under-ship
    /// tolerance, and moves the order to Shipped or Partially shipped to match. Orders that are draft, completed or
    /// cancelled keep their status. Returns the lines this call newly closed within tolerance, with the quantity closed.
    /// </summary>
    public static IReadOnlyList<(SalesOrderLine Line, decimal Closed)> RecordShipped(
        SalesOrder order, Func<SalesOrderLine, QuantityTolerance>? tolerance = null)
    {
        var closedNow = new List<(SalesOrderLine, decimal)>();
        foreach (var line in order.Lines)
        {
            line.ShippedQuantity = QuantityOn(order, line.Id, s => ShippedStatuses.Contains(s.Status));
            var closed = ApplyTolerance(order, line, tolerance?.Invoke(line) ?? QuantityTolerance.None);
            if (closed > 0m)
                closedNow.Add((line, closed));
        }

        if (!FulfilmentStatuses.Contains(order.Status))
            return closedNow;

        var goodsLines = order.Lines.Where(l => l.ShipsGoods).ToList();
        if (goodsLines.Count > 0 && goodsLines.All(l => l.IsFullyShipped))
            order.Status = SalesOrderStatus.Shipped;
        else if (order.Lines.Any(l => l.ShippedQuantity > 0))
            order.Status = SalesOrderStatus.PartiallyShipped;
        return closedNow;
    }

    private static decimal ApplyTolerance(SalesOrder order, SalesOrderLine line, QuantityTolerance tolerance)
    {
        var previouslyClosed = line.ClosedWithinTolerance ? line.ShortClosedQuantity : 0m;
        if (line.ShortCloseReason == SalesOrderLine.WithinToleranceReason)
        {
            line.ShortClosedQuantity = 0m;
            line.ShortCloseReason = null;
        }

        var open = line.Quantity - line.ShippedQuantity;
        var onTheWay = QuantityOn(order, line.Id, s => s.Status != ShipmentStatus.Cancelled) > line.ShippedQuantity;
        if (line.IsFreight || open <= 0m || line.ShippedQuantity <= 0m || onTheWay
            || !tolerance.Completes(line.Quantity, line.ShippedQuantity))
            return 0m;

        line.ShortClosedQuantity = open;
        line.ShortCloseReason = SalesOrderLine.WithinToleranceReason;
        return Math.Max(0m, open - previouslyClosed);
    }

    private static decimal QuantityOn(SalesOrder order, int salesOrderLineId, Func<Shipment, bool> counts)
        => order.Shipments
            .Where(counts)
            .SelectMany(s => s.Lines)
            .Where(l => l.SalesOrderLineId == salesOrderLineId)
            .Sum(l => l.Quantity);
}
