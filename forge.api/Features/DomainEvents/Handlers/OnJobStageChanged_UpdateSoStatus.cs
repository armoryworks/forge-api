using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>
/// Production progress moves the SO from Confirmed → InProduction when its first job enters a production
/// stage. It deliberately does NOT advance the SO to Shipped: fulfillment status is owned solely by actual
/// <see cref="Shipment"/>s (OnShipmentCreated_UpdateSalesOrder), so a "Shipped" order is always backed by a
/// real shipment + inventory relief + tracking. The job kanban reaching a ship/complete column instead
/// raises a ready-to-ship signal (OnJobStageChanged_CheckShipReady) — it never marks the order shipped on
/// its own. (This resolves the prior dual-writer conflict where a job dragged to a "Shipped" column flipped
/// the SO to Shipped with no shipment, stranding it short of Completed.)
/// </summary>
public class OnJobStageChanged_UpdateSoStatus(
    AppDbContext db,
    ILogger<OnJobStageChanged_UpdateSoStatus> logger)
    : INotificationHandler<JobStageChangedEvent>
{
    private static readonly HashSet<string> ProductionStageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "In Production", "Materials Received", "QC/Review"
    };

    public async Task Handle(JobStageChangedEvent notification, CancellationToken ct)
    {
        var job = await db.Jobs
            .Include(j => j.SalesOrderLine)
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == notification.JobId, ct);

        if (job?.SalesOrderLineId is null) return;

        var salesOrderId = job.SalesOrderLine!.SalesOrderId;

        var salesOrder = await db.SalesOrders.FirstOrDefaultAsync(so => so.Id == salesOrderId, ct);
        if (salesOrder is null) return;

        var toStage = await db.JobStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == notification.ToStageId, ct);
        if (toStage is null) return;

        var isProductionStage = ProductionStageNames.Contains(toStage.Name)
            || toStage.Name.Contains("Production", StringComparison.OrdinalIgnoreCase);

        // Confirmed → InProduction when the first job enters a production stage. (No Shipped transition —
        // that's the shipment's job; see the class summary.)
        if (salesOrder.Status != SalesOrderStatus.Confirmed || !isProductionStage) return;

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
        logger.LogInformation("SO {SalesOrderId} → InProduction (job {JobNumber} entered {Stage})",
            salesOrderId, job.JobNumber, toStage.Name);
    }
}
