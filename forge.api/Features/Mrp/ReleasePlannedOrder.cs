using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Mrp;

public record ReleasePlannedOrderCommand(int Id) : IRequest<ReleasePlannedOrderResult>;

public record ReleasePlannedOrderResult(int PlannedOrderId, string OrderType, int? CreatedPurchaseOrderId, int? CreatedJobId);

public class ReleasePlannedOrderHandler(
    AppDbContext db,
    IBarcodeService barcodeService,
    IPurchaseOrderRepository poRepo,
    IJobRepository jobRepo,
    IBusinessIdentifierService identifiers,
    IVendorCostResolver vendorCostResolver,
    ICurrencyService currencyService)
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

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (order.OrderType == MrpOrderType.Purchase)
        {
            var vendorId = order.Part?.PreferredVendorId;
            if (!vendorId.HasValue)
                throw new InvalidOperationException($"Part {order.Part?.PartNumber} has no preferred vendor. Assign one before releasing as a purchase order.");

            // Phase 3 / WU-10 — OrderedQuantity is decimal now; preserve the
            // Math.Ceiling rounding because MRP planned quantities have always
            // rounded up (whole-unit purchase increments).
            var orderedQuantity = Math.Ceiling(order.Quantity);
            var (unitPrice, currency) = await ResolveUnitPriceAsync(order.PartId, vendorId.Value, orderedQuantity, cancellationToken);

            var poNumber = await poRepo.GenerateNextPONumberAsync(cancellationToken);
            var po = new PurchaseOrder
            {
                PONumber = poNumber,
                VendorId = vendorId.Value,
                Status = PurchaseOrderStatus.Draft,
                ExpectedDeliveryDate = order.DueDate,
                QuoteCurrency = currency,
                Notes = $"Auto-generated from MRP planned order {order.Id}",
                // S4b provenance — released from an MRP planned order.
                OriginSource = PoOriginSource.AutoMrp,
                OriginReference = $"MRP planned order #{order.Id}",
            };
            po.Lines.Add(new PurchaseOrderLine
            {
                PartId = order.PartId,
                Description = order.Part?.Description ?? "",
                OrderedQuantity = orderedQuantity,
                UnitPrice = unitPrice,
                MrpPlannedOrderId = order.Id,
            });
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync(cancellationToken);

            await identifiers.IssueAsync(BusinessEntityType.PurchaseOrder, po.Id, po.PONumber, cancellationToken);

            db.LogActivityAt(
                "created",
                $"Purchase order {po.PONumber} created from MRP planned order #{order.Id}",
                ("PurchaseOrder", po.Id));

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

            var jobNumber = await jobRepo.GenerateNextJobNumberAsync(cancellationToken);
            var maxPosition = await jobRepo.GetMaxBoardPositionAsync(defaultStage.Id, cancellationToken);
            var job = new Job
            {
                JobNumber = jobNumber,
                Title = $"MRP: {order.Part?.PartNumber} x {order.Quantity:N0}",
                TrackTypeId = defaultTrackType.Id,
                CurrentStageId = defaultStage.Id,
                BoardPosition = maxPosition + 1,
                PartId = order.PartId,
                StartDate = order.StartDate,
                DueDate = order.DueDate,
                MrpPlannedOrderId = order.Id,
            };
            job.JobParts.Add(new JobPart
            {
                PartId = order.PartId,
                Quantity = order.Quantity,
            });
            job.ActivityLogs.Add(new JobActivityLog
            {
                Action = ActivityAction.Created,
                Description = $"Job {jobNumber} created from MRP planned order #{order.Id}.",
            });
            db.Jobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);

            await identifiers.IssueAsync(BusinessEntityType.Job, job.Id, job.JobNumber, cancellationToken);

            order.ReleasedJobId = job.Id;
            createdJobId = job.Id;

            await barcodeService.CreateBarcodeAsync(BarcodeEntityType.Job, job.Id, jobNumber, cancellationToken);
        }

        order.Status = MrpPlannedOrderStatus.Released;
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new ReleasePlannedOrderResult(order.Id, order.OrderType.ToString(), createdPoId, createdJobId);
    }

    private async Task<(decimal UnitPrice, string Currency)> ResolveUnitPriceAsync(
        int partId, int vendorId, decimal quantity, CancellationToken cancellationToken)
    {
        var vendorCost = await vendorCostResolver.ResolveForVendorAsync(partId, vendorId, quantity, cancellationToken);
        if (vendorCost.Resolved)
            return (vendorCost.CostPerBaseUnit, vendorCost.Currency);

        var manualCost = await db.Parts.AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => p.ManualCostOverride)
            .FirstOrDefaultAsync(cancellationToken);
        if (manualCost is decimal cost)
            return (cost, await currencyService.GetBaseCurrencyAsync(cancellationToken));

        var vendorCurrency = await db.VendorParts.AsNoTracking()
            .Where(vp => vp.PartId == partId && vp.VendorId == vendorId)
            .Select(vp => vp.Currency)
            .FirstOrDefaultAsync(cancellationToken);
        return (0m, vendorCurrency ?? await currencyService.GetBaseCurrencyAsync(cancellationToken));
    }
}
