using MediatR;
using Microsoft.EntityFrameworkCore;

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

        // Find QC inspections for this lot
        var inspections = await db.QcInspections
            .AsNoTracking()
            .Where(i => i.LotNumber == request.LotNumber)
            .Select(i => new LotTraceInspectionModel(
                i.Id,
                i.Status,
                db.Users.Where(u => u.Id == i.InspectorId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() ?? "",
                i.CreatedAt))
            .ToListAsync(cancellationToken);

        return new LotTraceabilityResponseModel(
            lot.LotNumber,
            lot.Part.PartNumber,
            lot.Part.Description,
            jobs,
            productionRuns,
            purchaseOrders,
            binLocations,
            inspections);
    }
}
