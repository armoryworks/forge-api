using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnJobStageChanged_UpdateSoStatus(
    AppDbContext db,
    ILogger<OnJobStageChanged_UpdateSoStatus> logger)
    : INotificationHandler<JobStageChangedEvent>
{
    private static readonly HashSet<string> CompletionStageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shipped", "Invoiced/Sent", "Payment Received", "Completed"
    };

    private static readonly HashSet<string> ProductionStageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "In Production", "Materials Received", "QC/Review"
    };

    public async Task Handle(JobStageChangedEvent notification, CancellationToken ct)
    {
        // Load the job with its SO line linkage
        var job = await db.Jobs
            .Include(j => j.SalesOrderLine)
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == notification.JobId, ct);

        if (job?.SalesOrderLineId is null) return;

        var salesOrderId = job.SalesOrderLine!.SalesOrderId;

        // Load the SO for status update (tracked, not AsNoTracking)
        var salesOrder = await db.SalesOrders
            .FirstOrDefaultAsync(so => so.Id == salesOrderId, ct);

        if (salesOrder is null) return;

        var toStage = await db.JobStages
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.ToStageId, ct);

        if (toStage is null) return;

        var isProductionStage = ProductionStageNames.Contains(toStage.Name) ||
                                toStage.Name.Contains("Production", StringComparison.OrdinalIgnoreCase);

        var isCompletionStage = CompletionStageNames.Contains(toStage.Name) ||
                                toStage.Name.Contains("Ship", StringComparison.OrdinalIgnoreCase);

        // Transition 1: Confirmed → InProduction when any job enters a production stage
        if (salesOrder.Status == SalesOrderStatus.Confirmed && isProductionStage)
        {
            salesOrder.Status = SalesOrderStatus.InProduction;

            db.ActivityLogs.Add(new ActivityLog
            {
                EntityType = "SalesOrder",
                EntityId = salesOrderId,
                UserId = notification.UserId,
                Action = "status_changed",
                Description = $"Status changed to In Production (job {job.JobNumber} entered {toStage.Name}).",
            });

            await db.SaveChangesAsync(ct);
            logger.LogInformation("SO {SalesOrderId} status updated to InProduction — job {JobNumber} entered {Stage}",
                salesOrderId, job.JobNumber, toStage.Name);
            return;
        }

        // Transition 2: Check if all jobs across all SO lines are complete → SO status = Shipped
        if (!isCompletionStage) return;
        if (salesOrder.Status is SalesOrderStatus.Shipped or SalesOrderStatus.Completed or SalesOrderStatus.Cancelled)
            return;

        // Load all jobs linked to any line of this SO
        var allSoJobs = await db.Jobs
            .Include(j => j.CurrentStage)
            .Where(j => j.SalesOrderLine != null && j.SalesOrderLine.SalesOrderId == salesOrderId)
            .AsNoTracking()
            .ToListAsync(ct);

        if (allSoJobs.Count == 0) return;

        var allComplete = allSoJobs.All(j =>
            CompletionStageNames.Contains(j.CurrentStage.Name) ||
            j.CurrentStage.Name.Contains("Ship", StringComparison.OrdinalIgnoreCase));

        if (!allComplete) return;

        salesOrder.Status = SalesOrderStatus.Shipped;

        db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "SalesOrder",
            EntityId = salesOrderId,
            UserId = notification.UserId,
            Action = "status_changed",
            Description = "Status changed to Shipped — all production jobs are complete.",
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("SO {SalesOrderId} status updated to Shipped — all {Count} jobs complete",
            salesOrderId, allSoJobs.Count);
    }
}
