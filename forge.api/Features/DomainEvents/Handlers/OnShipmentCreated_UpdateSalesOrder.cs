using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
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
            .FirstOrDefaultAsync(s => s.Id == notification.SalesOrderId, ct);

        if (so is null) return;

        db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "SalesOrder",
            EntityId = so.Id,
            UserId = notification.UserId,
            Action = "shipment_created",
            Description = $"Shipment {shipment.ShipmentNumber} created.",
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Logged shipment {ShipmentNumber} on SO {OrderNumber}", shipment.ShipmentNumber, so.OrderNumber);
    }
}
