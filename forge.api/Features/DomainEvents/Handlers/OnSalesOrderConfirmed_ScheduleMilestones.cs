using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnSalesOrderConfirmed_ScheduleMilestones(
    AppDbContext db,
    BackwardSchedulingService scheduler,
    ILogger<OnSalesOrderConfirmed_ScheduleMilestones> logger)
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
            logger.LogWarning("SalesOrder {Id} not found for milestone scheduling", notification.SalesOrderId);
            return;
        }

        var linesWithDelivery = so.Lines
            .Where(l => so.RequestedDeliveryDate.HasValue)
            .ToList();

        if (linesWithDelivery.Count == 0)
        {
            logger.LogInformation("No delivery dates on SO {OrderNumber} — skipping milestone scheduling", so.OrderNumber);
            return;
        }

        // Remove any existing milestones for these lines to avoid duplicates on re-confirmation
        var lineIds = linesWithDelivery.Select(l => l.Id).ToList();
        var existingMilestones = await db.ScheduleMilestones
            .Where(m => lineIds.Contains(m.SalesOrderLineId))
            .ToListAsync(ct);

        if (existingMilestones.Count > 0)
        {
            db.ScheduleMilestones.RemoveRange(existingMilestones);
        }

        // Find associated jobs for linking milestones
        var jobsByLine = await db.Jobs
            .Where(j => j.SalesOrderLineId.HasValue && lineIds.Contains(j.SalesOrderLineId.Value))
            .GroupBy(j => j.SalesOrderLineId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.First().Id, ct);

        var milestonesCreated = 0;

        foreach (var line in linesWithDelivery)
        {
            BackwardSchedule schedule;
            try
            {
                schedule = await scheduler.CalculateSchedule(line.Id, ct);
            }
            catch (KeyNotFoundException)
            {
                logger.LogWarning("Could not calculate schedule for SO line {LineId}", line.Id);
                continue;
            }

            jobsByLine.TryGetValue(line.Id, out var jobId);

            var milestones = new List<ScheduleMilestone>
            {
                new()
                {
                    SalesOrderLineId = line.Id,
                    JobId = jobId > 0 ? jobId : null,
                    MilestoneType = "po_order_by",
                    TargetDate = schedule.PoOrderBy,
                },
                new()
                {
                    SalesOrderLineId = line.Id,
                    JobId = jobId > 0 ? jobId : null,
                    MilestoneType = "materials_needed_by",
                    TargetDate = schedule.MaterialsNeededBy,
                },
                new()
                {
                    SalesOrderLineId = line.Id,
                    JobId = jobId > 0 ? jobId : null,
                    MilestoneType = "production_start_by",
                    TargetDate = schedule.ProductionStartBy,
                },
                new()
                {
                    SalesOrderLineId = line.Id,
                    JobId = jobId > 0 ? jobId : null,
                    MilestoneType = "production_complete_by",
                    TargetDate = schedule.ProductionCompleteBy,
                },
                new()
                {
                    SalesOrderLineId = line.Id,
                    JobId = jobId > 0 ? jobId : null,
                    MilestoneType = "qc_complete_by",
                    TargetDate = schedule.QcCompleteBy,
                },
                new()
                {
                    SalesOrderLineId = line.Id,
                    JobId = jobId > 0 ? jobId : null,
                    MilestoneType = "ship_by",
                    TargetDate = schedule.ShipBy,
                },
                new()
                {
                    SalesOrderLineId = line.Id,
                    JobId = jobId > 0 ? jobId : null,
                    MilestoneType = "delivery",
                    TargetDate = schedule.DeliveryDate,
                },
            };

            db.ScheduleMilestones.AddRange(milestones);
            milestonesCreated += milestones.Count;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created {Count} schedule milestones for SO {OrderNumber}",
            milestonesCreated, so.OrderNumber);
    }
}
