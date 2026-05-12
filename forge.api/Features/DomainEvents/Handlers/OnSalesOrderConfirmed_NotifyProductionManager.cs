using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnSalesOrderConfirmed_NotifyProductionManager(
    AppDbContext db,
    IHubContext<NotificationHub> notificationHub,
    ILogger<OnSalesOrderConfirmed_NotifyProductionManager> logger)
    : INotificationHandler<SalesOrderConfirmedEvent>
{
    public async Task Handle(SalesOrderConfirmedEvent notification, CancellationToken ct)
    {
        var so = await db.SalesOrders
            .Include(s => s.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.SalesOrderId, ct);

        if (so is null) return;

        var managerUserIds = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "Admin" || x.Name == "Manager")
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var managerId in managerUserIds)
        {
            var notif = new Notification
            {
                UserId = managerId,
                Type = "sales_order_confirmed",
                Severity = "info",
                Source = "sales_orders",
                Title = "Sales Order Confirmed",
                Message = $"SO-{so.OrderNumber} for {so.Customer?.Name ?? "Unknown"} has been confirmed and is ready for production planning.",
                EntityType = "SalesOrder",
                EntityId = so.Id,
                SenderId = notification.UserId,
            };

            db.Notifications.Add(notif);
        }

        await db.SaveChangesAsync(ct);

        // Push via SignalR
        foreach (var managerId in managerUserIds)
        {
            await notificationHub.Clients.Group($"user:{managerId}")
                .SendAsync("notificationReceived", new { type = "sales_order_confirmed", salesOrderId = so.Id }, ct);
        }

        logger.LogInformation("Notified {Count} manager(s) about confirmed SO {OrderNumber}", managerUserIds.Count, so.OrderNumber);
    }
}
