using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record ReleasePlannedOrderCommand(int Id) : IRequest<ReleasePlannedOrderResult>;

public record ReleasePlannedOrderResult(int PlannedOrderId, string OrderType, int? CreatedPurchaseOrderId, int? CreatedJobId);

public class ReleasePlannedOrderHandler(AppDbContext db, IClock clock, IBarcodeService barcodeService)
    : IRequestHandler<ReleasePlannedOrderCommand, ReleasePlannedOrderResult>
{
    public async Task<ReleasePlannedOrderResult> Handle(ReleasePlannedOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await db.MrpPlannedOrders
            .Include(po => po.Part)
            .FirstOrDefaultAsync(po => po.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Planned order {request.Id} not found.");

        if (order.Status == MrpPlannedOrderStatus.Released)
            throw new InvalidOperationException("This planned order has already been released.");

        if (order.Status == MrpPlannedOrderStatus.Cancelled)
            throw new InvalidOperationException("Cannot release a cancelled planned order.");

        int? createdPoId = null;
        int? createdJobId = null;

        if (order.OrderType == MrpOrderType.Purchase)
        {
            var vendorId = order.Part?.PreferredVendorId;
            if (!vendorId.HasValue)
                throw new InvalidOperationException($"Part {order.Part?.PartNumber} has no preferred vendor. Assign one before releasing as a purchase order.");

            var poNumber = $"PO-MRP-{clock.UtcNow:yyyyMMddHHmmss}";
            var po = new PurchaseOrder
            {
                PONumber = poNumber,
                VendorId = vendorId.Value,
                Status = PurchaseOrderStatus.Draft,
                Notes = $"Auto-generated from MRP planned order {order.Id}",
            };
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync(cancellationToken);

            var poLine = new PurchaseOrderLine
            {
                PurchaseOrderId = po.Id,
                PartId = order.PartId,
                Description = order.Part?.Description ?? "",
                // Phase 3 / WU-10 — OrderedQuantity is decimal now; preserve the
                // Math.Ceiling rounding because MRP planned quantities have always
                // rounded up (whole-unit purchase increments).
                OrderedQuantity = Math.Ceiling(order.Quantity),
                UnitPrice = 0,
                MrpPlannedOrderId = order.Id,
            };
            db.PurchaseOrderLines.Add(poLine);

            order.ReleasedPurchaseOrderId = po.Id;
            createdPoId = po.Id;

            await barcodeService.CreateBarcodeAsync(BarcodeEntityType.PurchaseOrder, po.Id, poNumber, cancellationToken);
        }
        else
        {
            // Create a manufacturing job
            var defaultTrackType = await db.TrackTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IsActive, cancellationToken);

            var defaultStage = defaultTrackType != null
                ? await db.JobStages.AsNoTracking()
                    .Where(s => s.TrackTypeId == defaultTrackType.Id)
                    .OrderBy(s => s.SortOrder)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            if (defaultTrackType == null || defaultStage == null)
                throw new InvalidOperationException("No active track type with stages found. Configure track types before releasing manufacturing orders.");

            var jobNumber = $"JOB-MRP-{clock.UtcNow:yyyyMMddHHmmss}";
            var job = new Job
            {
                JobNumber = jobNumber,
                Title = $"MRP: {order.Part?.PartNumber} x {order.Quantity:N0}",
                TrackTypeId = defaultTrackType.Id,
                CurrentStageId = defaultStage.Id,
                PartId = order.PartId,
                DueDate = order.DueDate,
                MrpPlannedOrderId = order.Id,
            };
            db.Jobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);

            order.ReleasedJobId = job.Id;
            createdJobId = job.Id;

            await barcodeService.CreateBarcodeAsync(BarcodeEntityType.Job, job.Id, jobNumber, cancellationToken);
        }

        order.Status = MrpPlannedOrderStatus.Released;
        await db.SaveChangesAsync(cancellationToken);

        return new ReleasePlannedOrderResult(order.Id, order.OrderType.ToString(), createdPoId, createdJobId);
    }
}
