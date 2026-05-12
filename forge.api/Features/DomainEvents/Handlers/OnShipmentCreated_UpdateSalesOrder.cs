using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnShipmentCreated_UpdateSalesOrder(
    AppDbContext db,
    ILogger<OnShipmentCreated_UpdateSalesOrder> logger)
    : INotificationHandler<ShipmentCreatedEvent>
{
    public async Task Handle(ShipmentCreatedEvent notification, CancellationToken ct)
    {
        var shipment = await db.Shipments
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == notification.ShipmentId, ct);

        if (shipment is null) return;

        var so = await db.SalesOrders
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == notification.SalesOrderId, ct);

        if (so is null) return;

        // Update ShippedQuantity on each SO line
        foreach (var shipmentLine in shipment.Lines)
        {
            if (shipmentLine.SalesOrderLineId is null) continue;

            var soLine = so.Lines.FirstOrDefault(l => l.Id == shipmentLine.SalesOrderLineId);
            if (soLine is null) continue;

            soLine.ShippedQuantity += shipmentLine.Quantity;
        }

        // Update SO status based on fulfillment
        var allFullyShipped = so.Lines.All(l => l.IsFullyShipped);
        var anyShipped = so.Lines.Any(l => l.ShippedQuantity > 0);

        if (allFullyShipped)
            so.Status = SalesOrderStatus.Shipped;
        else if (anyShipped)
            so.Status = SalesOrderStatus.PartiallyShipped;

        // Log activity
        db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "SalesOrder",
            EntityId = so.Id,
            UserId = notification.UserId,
            Action = "shipment_created",
            Description = $"Shipment {shipment.ShipmentNumber} created. SO status: {so.Status}.",
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Updated SO {OrderNumber} shipped quantities from shipment {ShipmentNumber}. Status: {Status}",
            so.OrderNumber, shipment.ShipmentNumber, so.Status);
    }
}
