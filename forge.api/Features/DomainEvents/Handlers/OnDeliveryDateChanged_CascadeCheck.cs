using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnDeliveryDateChanged_CascadeCheck(
    AppDbContext db,
    IClock clock,
    ILogger<OnDeliveryDateChanged_CascadeCheck> logger)
    : INotificationHandler<DeliveryDateChangedEvent>
{
    public async Task Handle(DeliveryDateChangedEvent notification, CancellationToken ct)
    {
        var soLine = await db.SalesOrderLines
            .Include(l => l.SalesOrder)
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == notification.SalesOrderLineId, ct);

        if (soLine is null) return;

        // Only check if the new date is earlier (more compressed)
        if (notification.NewDate >= notification.OldDate) return;

        // Check if any linked jobs have due dates after the new delivery date
        var linkedJobs = await db.Jobs
            .Where(j => j.SalesOrderLineId == soLine.Id && j.DueDate.HasValue)
            .AsNoTracking()
            .ToListAsync(ct);

        var atRiskJobs = linkedJobs
            .Where(j => j.DueDate!.Value > notification.NewDate)
            .ToList();

        if (atRiskJobs.Count == 0) return;

        // Check for existing follow-up
        var exists = await db.FollowUpTasks
            .AnyAsync(f =>
                f.SourceEntityType == "SalesOrderLine" &&
                f.SourceEntityId == soLine.Id &&
                f.TriggerType == FollowUpTriggerType.DeliveryAtRisk &&
                f.Status == FollowUpStatus.Open, ct);

        if (exists) return;

        var assigneeId = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "Manager" || x.Name == "PM" || x.Name == "Admin")
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);

        if (assigneeId == 0) return;

        db.FollowUpTasks.Add(new FollowUpTask
        {
            Title = $"Delivery at risk for SO-{soLine.SalesOrder.OrderNumber} line {soLine.LineNumber}",
            Description = $"Delivery date moved from {notification.OldDate:MM/dd/yyyy} to {notification.NewDate:MM/dd/yyyy}. {atRiskJobs.Count} job(s) have due dates after the new delivery date and may need rescheduling.",
            AssignedToUserId = assigneeId,
            DueDate = clock.UtcNow,
            SourceEntityType = "SalesOrderLine",
            SourceEntityId = soLine.Id,
            TriggerType = FollowUpTriggerType.DeliveryAtRisk,
            Status = FollowUpStatus.Open,
        });

        await db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Delivery at risk for SO {OrderNumber} line {Line}: {Count} job(s) may miss new deadline",
            soLine.SalesOrder.OrderNumber, soLine.LineNumber, atRiskJobs.Count);
    }
}
