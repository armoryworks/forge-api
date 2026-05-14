using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnSalesOrderConfirmed_CreateFollowUps(
    AppDbContext db,
    IClock clock,
    ILogger<OnSalesOrderConfirmed_CreateFollowUps> logger)
    : INotificationHandler<SalesOrderConfirmedEvent>
{
    public async Task Handle(SalesOrderConfirmedEvent notification, CancellationToken ct)
    {
        var so = await db.SalesOrders
            .Include(s => s.Lines)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.SalesOrderId, ct);

        if (so is null)
        {
            logger.LogWarning("SalesOrder {Id} not found for follow-up creation", notification.SalesOrderId);
            return;
        }

        // Find PM/Manager users to assign follow-ups
        var managerUserIds = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "Admin" || x.Name == "Manager" || x.Name == "PM")
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        var assigneeId = managerUserIds.FirstOrDefault();
        if (assigneeId == 0)
        {
            logger.LogWarning("No PM/Manager found for follow-up assignment on SO {OrderNumber}", so.OrderNumber);
            return;
        }

        // Check which SO lines have no jobs yet
        var linesWithoutJobs = new List<SalesOrderLine>();
        foreach (var line in so.Lines)
        {
            var hasJobs = await db.Jobs
                .AnyAsync(j => j.SalesOrderLineId == line.Id, ct);

            if (!hasJobs)
                linesWithoutJobs.Add(line);
        }

        if (linesWithoutJobs.Count > 0)
        {
            var now = clock.UtcNow;
            db.FollowUpTasks.Add(new FollowUpTask
            {
                Title = $"Create jobs for SO-{so.OrderNumber}",
                Description = $"{linesWithoutJobs.Count} line(s) in Sales Order {so.OrderNumber} need production jobs created.",
                AssignedToUserId = assigneeId,
                DueDate = now.AddDays(1),
                SourceEntityType = "SalesOrder",
                SourceEntityId = so.Id,
                TriggerType = FollowUpTriggerType.SalesOrderConfirmed,
                Status = FollowUpStatus.Open,
            });
        }

        // Log activity
        db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "SalesOrder",
            EntityId = so.Id,
            UserId = notification.UserId,
            Action = "confirmed",
            Description = $"Sales Order {so.OrderNumber} confirmed. Follow-up tasks created.",
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created follow-up tasks for confirmed SO {OrderNumber}", so.OrderNumber);
    }
}
