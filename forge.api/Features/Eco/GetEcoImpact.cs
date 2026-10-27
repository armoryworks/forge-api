using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

/// <summary>
/// The open work an ECO touches. For every affected part (an affected Part or BOM item, or the part of an
/// affected Operation item): open jobs that build it or an assembly that uses it (where-used up the
/// current BOMs, and the BOM revision a released job froze), open purchase-order lines with quantity
/// still to receive, open sales-order lines with quantity still to ship, and on-hand stock grouped by
/// the lot's part revision. A job is open while it is not completed, archived or disposed (a job held
/// for review is still open).
/// </summary>
public record GetEcoImpactQuery(int EcoId) : IRequest<EcoImpactResponseModel>;

public class GetEcoImpactHandler(AppDbContext db) : IRequestHandler<GetEcoImpactQuery, EcoImpactResponseModel>
{
    private const int MaxWhereUsedDepth = 10;

    private static readonly HashSet<string> PartEntityTypes = new(StringComparer.OrdinalIgnoreCase) { "Part", "BOM" };

    private static readonly PurchaseOrderStatus[] ClosedPoStatuses =
        [PurchaseOrderStatus.Received, PurchaseOrderStatus.Closed, PurchaseOrderStatus.Cancelled];

    private static readonly SalesOrderStatus[] ClosedSoStatuses =
        [SalesOrderStatus.Shipped, SalesOrderStatus.Completed, SalesOrderStatus.Cancelled];

    public async Task<EcoImpactResponseModel> Handle(GetEcoImpactQuery request, CancellationToken cancellationToken)
    {
        var items = await db.EngineeringChangeOrders.AsNoTracking()
            .Where(e => e.Id == request.EcoId)
            .Select(e => e.AffectedItems.Select(a => new { a.EntityType, a.EntityId }).ToList())
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"ECO {request.EcoId} not found");

        var partIds = items
            .Where(i => PartEntityTypes.Contains(i.EntityType))
            .Select(i => i.EntityId)
            .ToList();
        var operationIds = items
            .Where(i => string.Equals(i.EntityType, "Operation", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.EntityId)
            .ToList();
        if (operationIds.Count > 0)
            partIds.AddRange(await db.Operations.AsNoTracking()
                .Where(o => operationIds.Contains(o.Id))
                .Select(o => o.PartId)
                .ToListAsync(cancellationToken));

        var parts = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .OrderBy(p => p.PartNumber)
            .Select(p => new { p.Id, p.PartNumber, p.Name, p.Revision })
            .ToListAsync(cancellationToken);

        var result = new List<EcoPartImpactModel>();
        foreach (var part in parts)
        {
            result.Add(new EcoPartImpactModel(
                part.Id,
                part.PartNumber,
                part.Name,
                part.Revision,
                await OpenJobsAsync(part.Id, cancellationToken),
                await OpenPurchaseOrderLinesAsync(part.Id, cancellationToken),
                await OpenSalesOrderLinesAsync(part.Id, cancellationToken),
                await StockByRevisionAsync(part.Id, cancellationToken)));
        }

        return new EcoImpactResponseModel(request.EcoId, result);
    }

    private async Task<List<EcoImpactJobModel>> OpenJobsAsync(int partId, CancellationToken ct)
    {
        var parentPartIds = await WhereUsedAsync(partId, ct);
        var frozenBomRevisionIds = await db.BomRevisionLines.AsNoTracking()
            .Where(l => l.PartId == partId)
            .Select(l => l.BomRevisionId)
            .Distinct()
            .ToListAsync(ct);

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => !j.IsArchived && j.CompletedDate == null
                && (j.Disposition == null || j.Disposition == JobDisposition.HoldForReview)
                && (j.PartId == partId
                    || (j.PartId != null && parentPartIds.Contains(j.PartId.Value))
                    || (j.BomRevisionIdAtRelease != null && frozenBomRevisionIds.Contains(j.BomRevisionIdAtRelease.Value))))
            .OrderBy(j => j.DueDate == null).ThenBy(j => j.DueDate).ThenBy(j => j.JobNumber)
            .Select(j => new
            {
                j.Id,
                j.JobNumber,
                j.Title,
                j.PartId,
                PartNumber = j.Part != null ? j.Part.PartNumber : null,
                j.PartRevision,
                StageName = j.CurrentStage.Name,
                j.DueDate,
            })
            .ToListAsync(ct);

        return jobs
            .Select(j => new EcoImpactJobModel(
                j.Id, j.JobNumber, j.Title, j.PartId ?? partId, j.PartNumber, j.PartRevision, j.StageName, j.DueDate,
                j.PartId == partId ? "Direct" : "WhereUsed"))
            .ToList();
    }

    private async Task<HashSet<int>> WhereUsedAsync(int partId, CancellationToken ct)
    {
        var parents = new HashSet<int>();
        var frontier = new List<int> { partId };
        for (var depth = 0; depth < MaxWhereUsedDepth && frontier.Count > 0; depth++)
        {
            var current = frontier;
            var next = await db.BOMLines.AsNoTracking()
                .Where(l => current.Contains(l.ChildPartId))
                .Select(l => l.ParentPartId)
                .Distinct()
                .ToListAsync(ct);
            frontier = next.Where(id => id != partId && parents.Add(id)).ToList();
        }
        return parents;
    }

    private async Task<List<EcoImpactPurchaseOrderLineModel>> OpenPurchaseOrderLinesAsync(int partId, CancellationToken ct)
    {
        var lines = await db.PurchaseOrderLines.AsNoTracking()
            .Where(l => l.PartId == partId
                && !ClosedPoStatuses.Contains(l.PurchaseOrder.Status)
                && l.OrderedQuantity - l.ReceivedQuantity - l.CancelledShortCloseQuantity > 0)
            .OrderBy(l => l.PurchaseOrder.PONumber)
            .Select(l => new
            {
                l.PurchaseOrderId,
                l.PurchaseOrder.PONumber,
                l.Id,
                l.PurchaseOrder.Vendor.CompanyName,
                Open = l.OrderedQuantity - l.ReceivedQuantity - l.CancelledShortCloseQuantity,
                l.PurchaseOrder.Status,
            })
            .ToListAsync(ct);

        return lines
            .Select(l => new EcoImpactPurchaseOrderLineModel(
                l.PurchaseOrderId, l.PONumber, l.Id, l.CompanyName, l.Open, l.Status.ToString()))
            .ToList();
    }

    private async Task<List<EcoImpactSalesOrderLineModel>> OpenSalesOrderLinesAsync(int partId, CancellationToken ct)
    {
        var lines = await db.SalesOrderLines.AsNoTracking()
            .Where(l => l.PartId == partId
                && !ClosedSoStatuses.Contains(l.SalesOrder.Status)
                && l.Quantity - l.ShippedQuantity > 0)
            .OrderBy(l => l.SalesOrder.OrderNumber).ThenBy(l => l.LineNumber)
            .Select(l => new
            {
                l.SalesOrderId,
                l.SalesOrder.OrderNumber,
                l.Id,
                l.LineNumber,
                l.SalesOrder.Customer.Name,
                Open = l.Quantity - l.ShippedQuantity,
                l.SalesOrder.Status,
            })
            .ToListAsync(ct);

        return lines
            .Select(l => new EcoImpactSalesOrderLineModel(
                l.SalesOrderId, l.OrderNumber, l.Id, l.LineNumber, l.Name, l.Open, l.Status.ToString()))
            .ToList();
    }

    private async Task<List<EcoImpactStockModel>> StockByRevisionAsync(int partId, CancellationToken ct)
    {
        var bins = await db.BinContents.AsNoTracking()
            .Where(bc => bc.EntityType == "part" && bc.EntityId == partId && bc.RemovedAt == null && bc.Quantity > 0)
            .Select(bc => new { bc.LotNumber, bc.Quantity })
            .ToListAsync(ct);
        if (bins.Count == 0)
            return [];

        var lotNumbers = bins.Where(b => b.LotNumber != null).Select(b => b.LotNumber!).Distinct().ToList();
        var revisionByLot = (await db.LotRecords.AsNoTracking()
                .Where(l => l.PartId == partId && lotNumbers.Contains(l.LotNumber))
                .Select(l => new { l.LotNumber, l.PartRevision })
                .ToListAsync(ct))
            .GroupBy(l => l.LotNumber)
            .ToDictionary(g => g.Key, g => g.First().PartRevision);

        return bins
            .Select(b => new
            {
                Revision = b.LotNumber != null && revisionByLot.TryGetValue(b.LotNumber, out var rev) && !string.IsNullOrWhiteSpace(rev) ? rev : null,
                b.LotNumber,
                b.Quantity,
            })
            .GroupBy(b => b.Revision)
            .OrderBy(g => g.Key == null).ThenBy(g => g.Key)
            .Select(g => new EcoImpactStockModel(
                g.Key, g.Sum(x => x.Quantity), g.Where(x => x.LotNumber != null).Select(x => x.LotNumber).Distinct().Count()))
            .ToList();
    }
}
