using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Lots;

public record GetLotTraceabilityQuery(string LotNumber) : IRequest<LotTraceabilityResponseModel>;

public class GetLotTraceabilityHandler(AppDbContext db)
    : IRequestHandler<GetLotTraceabilityQuery, LotTraceabilityResponseModel>
{
    public async Task<LotTraceabilityResponseModel> Handle(
        GetLotTraceabilityQuery request, CancellationToken cancellationToken)
    {
        var lot = await db.LotRecords
            .AsNoTracking()
            .Include(l => l.Part)
            .FirstOrDefaultAsync(l => l.LotNumber == request.LotNumber, cancellationToken)
            ?? throw new KeyNotFoundException($"Lot '{request.LotNumber}' not found.");

        // Find all jobs linked to this lot
        var jobs = await db.LotRecords
            .AsNoTracking()
            .Where(l => l.LotNumber == request.LotNumber && l.JobId != null)
            .Select(l => new LotTraceJobModel(l.Job!.Id, l.Job.JobNumber, l.Job.Title))
            .Distinct()
            .ToListAsync(cancellationToken);

        // Find all production runs linked to this lot
        var productionRuns = await db.LotRecords
            .AsNoTracking()
            .Where(l => l.LotNumber == request.LotNumber && l.ProductionRunId != null)
            .Select(l => new LotTraceProductionRunModel(
                l.ProductionRun!.Id,
                l.ProductionRun.RunNumber,
                l.ProductionRun.Status.ToString()))
            .Distinct()
            .ToListAsync(cancellationToken);

        // Find purchase orders linked to this lot
        var purchaseOrders = await db.LotRecords
            .AsNoTracking()
            .Where(l => l.LotNumber == request.LotNumber && l.PurchaseOrderLineId != null)
            .Select(l => new LotTracePurchaseOrderModel(
                l.PurchaseOrderLine!.PurchaseOrder.Id,
                l.PurchaseOrderLine.PurchaseOrder.PONumber,
                l.PurchaseOrderLine.PurchaseOrder.Vendor.CompanyName))
            .Distinct()
            .ToListAsync(cancellationToken);

        // Find bin locations containing this part with matching lot number
        var binLocations = await db.BinContents
            .AsNoTracking()
            .Include(bc => bc.Location)
            .Where(bc => bc.EntityType == "part" && bc.EntityId == lot.PartId && bc.LotNumber == request.LotNumber)
            .Select(bc => new LotTraceBinLocationModel(
                bc.LocationId,
                bc.Location.Name,
                (int)bc.Quantity))
            .ToListAsync(cancellationToken);

        var receivedFrom = await db.ReceivingRecords
            .AsNoTracking()
            .Where(r => r.LotNumber == lot.LotNumber
                && (r.PurchaseOrderLine.PartId == null || r.PurchaseOrderLine.PartId == lot.PartId))
            .OrderBy(r => r.CreatedAt)
            .Select(r => new LotTraceReceiptModel(
                r.Id,
                r.ReceiptNumber,
                r.PurchaseOrderLine.PurchaseOrderId,
                r.PurchaseOrderLine.PurchaseOrder.PONumber,
                r.PurchaseOrderLine.PurchaseOrder.VendorId,
                r.PurchaseOrderLine.PurchaseOrder.Vendor.CompanyName,
                r.CreatedAt,
                r.QuantityReceived,
                r.InspectionStatus.ToString()))
            .ToListAsync(cancellationToken);

        var receiptInspectionIds = await db.ReceivingRecords
            .AsNoTracking()
            .Where(r => r.LotNumber == lot.LotNumber && r.QcInspectionId != null)
            .Select(r => r.QcInspectionId!.Value)
            .ToListAsync(cancellationToken);

        var inspections = await db.QcInspections
            .AsNoTracking()
            .Where(i => receiptInspectionIds.Contains(i.Id)
                || (i.LotNumber == lot.LotNumber
                    && (i.ProductionRunId != null
                        ? i.ProductionRun!.PartId == lot.PartId
                        : i.Job != null && i.Job.PartId != null
                            ? i.Job.PartId == lot.PartId
                            : i.Template == null || i.Template.PartId == null || i.Template.PartId == lot.PartId)))
            .OrderBy(i => i.CreatedAt)
            .Select(i => new LotTraceInspectionModel(
                i.Id,
                i.Status,
                db.Users.Where(u => u.Id == i.InspectorId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() ?? "",
                i.CreatedAt,
                i.Template != null ? i.Template.Name : null,
                i.CompletedAt,
                i.Results.Count(r => r.Passed),
                i.Results.Count(r => !r.Passed)))
            .ToListAsync(cancellationToken);

        var nonConformances = await db.NonConformances
            .AsNoTracking()
            .Where(n => n.LotNumber == lot.LotNumber && n.PartId == lot.PartId)
            .OrderBy(n => n.DetectedAt)
            .Select(n => new LotTraceNcrModel(
                n.Id,
                n.NcrNumber,
                n.Type.ToString(),
                n.Status.ToString(),
                n.DetectedAt,
                n.Description,
                n.AffectedQuantity,
                n.DispositionCode.HasValue ? n.DispositionCode.Value.ToString() : null))
            .ToListAsync(cancellationToken);

        var producingJob = await ResolveProducingJobAsync(lot, cancellationToken);

        // Flattened, date-ordered timeline — the shape the lot detail panel
        // actually renders. The categorized lists above stay for the quality
        // module; the panel previously read these (absent) fields and showed a
        // blank quantity and a dead timeline.
        var lotRows = await db.LotRecords
            .AsNoTracking()
            .Where(l => l.LotNumber == request.LotNumber)
            .Select(l => new
            {
                l.CreatedAt,
                l.Quantity,
                JobNumber = l.Job != null ? l.Job.JobNumber : null,
                JobTitle = l.Job != null ? l.Job.Title : null,
                RunNumber = l.ProductionRun != null ? l.ProductionRun.RunNumber : null,
                RunStatus = l.ProductionRun != null ? l.ProductionRun.Status.ToString() : null,
                PoNumber = l.PurchaseOrderLine != null ? l.PurchaseOrderLine.PurchaseOrder.PONumber : null,
                VendorName = l.PurchaseOrderLine != null ? l.PurchaseOrderLine.PurchaseOrder.Vendor.CompanyName : null,
            })
            .ToListAsync(cancellationToken);

        var binRows = await db.BinContents
            .AsNoTracking()
            .Where(bc => bc.EntityType == "part" && bc.EntityId == lot.PartId && bc.LotNumber == request.LotNumber)
            .Select(bc => new { bc.Location.Name, bc.Quantity, bc.PlacedAt })
            .ToListAsync(cancellationToken);

        // Component genealogy edges (regulated-parts-safety C-2). Backward = the input
        // lots consumed to make this lot; forward = the output lots this lot went into.
        var consumedLots = await db.LotConsumptions
            .AsNoTracking()
            .Where(c => c.ProducedLotId == lot.Id)
            .Include(c => c.ConsumedLot).ThenInclude(l => l.Part)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new LotConsumptionEdgeModel(
                c.Id, c.ConsumedLotId, c.ConsumedLot.LotNumber,
                c.ConsumedLot.PartId, c.ConsumedLot.Part.PartNumber,
                c.Quantity, c.JobId, c.ProductionRunId, c.CreatedAt))
            .ToListAsync(cancellationToken);

        var producedLots = await db.LotConsumptions
            .AsNoTracking()
            .Where(c => c.ConsumedLotId == lot.Id)
            .Include(c => c.ProducedLot).ThenInclude(l => l.Part)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new LotConsumptionEdgeModel(
                c.Id, c.ProducedLotId, c.ProducedLot.LotNumber,
                c.ProducedLot.PartId, c.ProducedLot.Part.PartNumber,
                c.Quantity, c.JobId, c.ProductionRunId, c.CreatedAt))
            .ToListAsync(cancellationToken);

        var events = new List<LotTraceEventModel>();
        foreach (var row in lotRows)
        {
            if (row.JobNumber != null)
                events.Add(new LotTraceEventModel("Job", row.JobNumber, row.JobTitle ?? string.Empty, row.CreatedAt, row.Quantity));
            if (row.RunNumber != null)
                events.Add(new LotTraceEventModel("ProductionRun", row.RunNumber, row.RunStatus ?? string.Empty, row.CreatedAt, row.Quantity, row.RunStatus));
            if (row.PoNumber != null)
                events.Add(new LotTraceEventModel("PurchaseOrder", row.PoNumber, row.VendorName ?? string.Empty, row.CreatedAt, row.Quantity));
        }
        events.AddRange(binRows.Select(b =>
            new LotTraceEventModel("BinLocation", b.Name, string.Empty, b.PlacedAt, b.Quantity)));
        events.AddRange(inspections.Select(i =>
        {
            var actor = string.IsNullOrWhiteSpace(i.InspectorName) ? null : i.InspectorName.Trim();
            var description = actor is null ? i.Status : $"{i.Status} — {actor}";
            return new LotTraceEventModel("QcInspection", $"QC #{i.Id}", description, i.CreatedAt, null, i.Status, actor);
        }));
        events.AddRange(consumedLots.Select(c =>
            new LotTraceEventModel("ConsumedInput", c.LotNumber, c.PartNumber, c.CreatedAt, c.Quantity)));
        events.AddRange(producedLots.Select(p =>
            new LotTraceEventModel("ConsumedInto", p.LotNumber, p.PartNumber, p.CreatedAt, p.Quantity)));
        events.Sort((a, b) => a.Date.CompareTo(b.Date));

        var shippedTo = (await LotShipmentResolver.ForLotsAsync(db, [lot.LotNumber], cancellationToken))
            .GroupBy(r => new { r.ShipmentId, r.CustomerId })
            .Select(g => new LotTraceShipmentModel(
                g.Key.ShipmentId,
                g.First().ShipmentNumber,
                g.Key.CustomerId,
                g.First().CustomerName,
                g.First().ShippedDate,
                g.Sum(r => r.Quantity)))
            .ToList();

        return new LotTraceabilityResponseModel(
            lot.LotNumber,
            lot.Part.PartNumber,
            lot.Part.Description,
            jobs,
            productionRuns,
            purchaseOrders,
            binLocations,
            inspections,
            lot.Quantity,
            lot.ExpirationDate,
            lot.SupplierLotNumber,
            events,
            consumedLots,
            producedLots,
            shippedTo,
            receivedFrom,
            nonConformances,
            producingJob);
    }

    private async Task<LotTraceJobModel?> ResolveProducingJobAsync(LotRecord lot, CancellationToken ct)
    {
        int? jobId = lot.JobId;
        if (jobId is null && lot.ProductionRunId is int runId)
        {
            jobId = await db.ProductionRuns
                .AsNoTracking()
                .Where(r => r.Id == runId)
                .Select(r => (int?)r.JobId)
                .FirstOrDefaultAsync(ct);
        }
        jobId ??= await db.LotConsumptions
            .AsNoTracking()
            .Where(c => c.ProducedLotId == lot.Id && c.JobId != null)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.JobId)
            .FirstOrDefaultAsync(ct);

        if (jobId is null) return null;
        return await db.Jobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new LotTraceJobModel(j.Id, j.JobNumber, j.Title))
            .FirstOrDefaultAsync(ct);
    }
}
