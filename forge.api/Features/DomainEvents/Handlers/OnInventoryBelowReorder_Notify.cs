using System.Globalization;

using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnInventoryBelowReorder_Notify(
    AppDbContext db,
    IHubContext<NotificationHub> notificationHub,
    ILogger<OnInventoryBelowReorder_Notify> logger)
    : INotificationHandler<InventoryBelowReorderEvent>
{
    internal const string NotificationType = "inventory_below_reorder";
    private static readonly string[] RecipientRoles = ["Procurement", "Manager", "Admin"];

    public async Task Handle(InventoryBelowReorderEvent notification, CancellationToken ct)
    {
        var part = await db.Parts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == notification.PartId, ct);

        if (part is null) return;

        var recipients = await AlertRecipients.ActiveUserIdsInRolesAsync(db, RecipientRoles, ct);
        var alreadyAlerted = await AlertRecipients.UsersWithOpenAlertAsync(db, NotificationType, "Part", part.Id, ct);
        var newRecipients = recipients.Where(id => !alreadyAlerted.Contains(id)).ToList();

        if (newRecipients.Count == 0) return;

        var label = string.IsNullOrWhiteSpace(part.Name) ? part.PartNumber : $"{part.PartNumber} ({part.Name})";
        var message = string.Create(CultureInfo.InvariantCulture,
            $"{label} has {notification.CurrentQty:N0} available, at or below its reorder point of {notification.ReorderPoint:N0}.");

        foreach (var userId in newRecipients)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType,
                Severity = "warning",
                Source = "inventory",
                Title = $"Low stock: {part.PartNumber}",
                Message = message,
                EntityType = "Part",
                EntityId = part.Id,
            });
        }

        await db.SaveChangesAsync(ct);

        foreach (var userId in newRecipients)
        {
            await notificationHub.Clients.Group($"user:{userId}")
                .SendAsync("notificationReceived", new { type = NotificationType, partId = part.Id }, ct);
        }

        logger.LogInformation("Notified {Count} user(s) that part {PartNumber} is below its reorder point",
            newRecipients.Count, part.PartNumber);
    }
}
