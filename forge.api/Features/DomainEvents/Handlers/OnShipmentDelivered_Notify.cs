using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnShipmentDelivered_Notify(
    AppDbContext db,
    IClock clock,
    IHubContext<NotificationHub> notificationHub,
    ILogger<OnShipmentDelivered_Notify> logger)
    : INotificationHandler<ShipmentDeliveredEvent>
{
    public async Task Handle(ShipmentDeliveredEvent notification, CancellationToken ct)
    {
        var shipment = await db.Shipments
            .Include(s => s.SalesOrder)
                .ThenInclude(so => so.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.ShipmentId, ct);

        if (shipment is null) return;

        var so = shipment.SalesOrder;
        var customerName = so.Customer?.Name ?? "Unknown";

        // Find the salesperson — look for the quote's assigned user or fall back to creator
        var assignedUserId = 0;
        if (so.QuoteId.HasValue)
        {
            var quote = await db.Quotes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == so.QuoteId, ct);
            assignedUserId = quote?.AssignedToId ?? 0;
        }

        if (assignedUserId == 0)
        {
            // Fall back to an Office Manager or PM
            assignedUserId = await db.UserRoles
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .Where(x => x.Name == "OfficeManager" || x.Name == "PM")
                .Select(x => x.UserId)
                .FirstOrDefaultAsync(ct);
        }

        if (assignedUserId > 0)
        {
            db.FollowUpTasks.Add(new FollowUpTask
            {
                Title = $"Shipment delivered — SO-{so.OrderNumber}",
                Description = $"Shipment {shipment.ShipmentNumber} for {customerName} has been delivered. Follow up on satisfaction and invoicing.",
                AssignedToUserId = assignedUserId,
                DueDate = clock.UtcNow.AddDays(2),
                SourceEntityType = "Shipment",
                SourceEntityId = shipment.Id,
                TriggerType = FollowUpTriggerType.ShipmentDelivered,
                Status = FollowUpStatus.Open,
            });

            // Also create notification
            var notif = new Notification
            {
                UserId = assignedUserId,
                Type = "shipment_delivered",
                Severity = "info",
                Source = "shipments",
                Title = "Shipment Delivered",
                Message = $"Shipment {shipment.ShipmentNumber} for SO-{so.OrderNumber} ({customerName}) has been delivered.",
                EntityType = "Shipment",
                EntityId = shipment.Id,
            };
            db.Notifications.Add(notif);

            await db.SaveChangesAsync(ct);

            await notificationHub.Clients.Group($"user:{assignedUserId}")
                .SendAsync("notificationReceived", new { type = "shipment_delivered", shipmentId = shipment.Id }, ct);
        }

        logger.LogInformation("Shipment {ShipmentNumber} delivered for SO {OrderNumber}", shipment.ShipmentNumber, so.OrderNumber);
    }
}
