using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnJobStageChanged_CheckShipReady(
    AppDbContext db,
    IClock clock,
    IHubContext<NotificationHub> notificationHub,
    ILogger<OnJobStageChanged_CheckShipReady> logger)
    : INotificationHandler<JobStageChangedEvent>
{
    public async Task Handle(JobStageChangedEvent notification, CancellationToken ct)
    {
        var job = await db.Jobs
            .Include(j => j.CurrentStage)
            .Include(j => j.SalesOrderLine)
                .ThenInclude(l => l!.SalesOrder)
            .FirstOrDefaultAsync(j => j.Id == notification.JobId, ct);

        if (job?.SalesOrderLineId is null) return;

        var soLine = job.SalesOrderLine!;

        // Check if ALL jobs for this SO line are in a completed stage
        var allJobsForLine = await db.Jobs
            .Include(j => j.CurrentStage)
            .Where(j => j.SalesOrderLineId == soLine.Id)
            .ToListAsync(ct);

        // A stage is considered "complete" if it's past QC (last stage or shipped-type stages)
        var toStage = await db.JobStages
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.ToStageId, ct);

        if (toStage is null) return;

        // Check if the stage name indicates completion (QC passed, Shipped, etc.)
        var completionStageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shipped", "Invoiced/Sent", "Payment Received", "Completed"
        };

        // Only proceed if the job just moved to a completion-type stage
        if (!completionStageNames.Contains(toStage.Name) &&
            !toStage.Name.Contains("Ship", StringComparison.OrdinalIgnoreCase))
            return;

        // Check if all sibling jobs for this line are at completion stages
        var allComplete = allJobsForLine.All(j =>
            completionStageNames.Contains(j.CurrentStage.Name) ||
            j.CurrentStage.Name.Contains("Ship", StringComparison.OrdinalIgnoreCase));

        if (!allComplete) return;

        // Check if a ship-ready follow-up already exists for this SO line
        var existingFollowUp = await db.FollowUpTasks
            .AnyAsync(f =>
                f.SourceEntityType == "SalesOrderLine" &&
                f.SourceEntityId == soLine.Id &&
                f.TriggerType == FollowUpTriggerType.ShipReady &&
                f.Status == FollowUpStatus.Open, ct);

        if (existingFollowUp) return;

        // Find all shipping-responsible users (OfficeManager role)
        var shippingUserIds = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "OfficeManager" || x.Name == "Manager" || x.Name == "Admin")
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        if (shippingUserIds.Count == 0) return;

        var primaryAssigneeId = shippingUserIds[0];

        db.FollowUpTasks.Add(new FollowUpTask
        {
            Title = $"SO line ready to ship — SO-{soLine.SalesOrder.OrderNumber}",
            Description = $"All production jobs for line {soLine.LineNumber} are complete. Ready to create shipment.",
            AssignedToUserId = primaryAssigneeId,
            DueDate = clock.UtcNow.AddDays(1),
            SourceEntityType = "SalesOrderLine",
            SourceEntityId = soLine.Id,
            TriggerType = FollowUpTriggerType.ShipReady,
            Status = FollowUpStatus.Open,
        });

        // Create notifications for all shipping coordinators
        foreach (var userId in shippingUserIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = "ship_ready",
                Severity = "info",
                Source = "sales_orders",
                Title = "Ready to Ship",
                Message = $"All jobs for SO-{soLine.SalesOrder.OrderNumber} line {soLine.LineNumber} are complete. Ready to create shipment.",
                EntityType = "SalesOrder",
                EntityId = soLine.SalesOrderId,
                SenderId = notification.UserId,
            });
        }

        db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "SalesOrder",
            EntityId = soLine.SalesOrderId,
            UserId = notification.UserId,
            Action = "ship_ready",
            Description = $"All jobs for SO line {soLine.LineNumber} are complete. Ready to ship.",
        });

        await db.SaveChangesAsync(ct);

        // Push via SignalR
        foreach (var userId in shippingUserIds)
        {
            await notificationHub.Clients.Group($"user:{userId}")
                .SendAsync("notificationReceived", new { type = "ship_ready", salesOrderId = soLine.SalesOrderId }, ct);
        }

        logger.LogInformation("SO line {LineId} for SO {OrderNumber} is ready to ship — notified {Count} user(s)",
            soLine.Id, soLine.SalesOrder.OrderNumber, shippingUserIds.Count);
    }
}
