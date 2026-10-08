using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record GetPartQualitySummaryQuery(int PartId) : IRequest<PartQualitySummaryResponseModel>;

public class GetPartQualitySummaryHandler(AppDbContext db)
    : IRequestHandler<GetPartQualitySummaryQuery, PartQualitySummaryResponseModel>
{
    private const int RecentInspectionLimit = 10;

    public async Task<PartQualitySummaryResponseModel> Handle(
        GetPartQualitySummaryQuery request, CancellationToken cancellationToken)
    {
        var part = await db.Parts
            .AsNoTracking()
            .Where(p => p.Id == request.PartId)
            .Select(p => new { p.Id, p.ReceivingInspectionTemplateId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var recentInspections = await db.QcInspections
            .AsNoTracking()
            .Where(i => (i.ProductionRunId != null && i.ProductionRun!.PartId == part.Id)
                || (i.Job != null && i.Job.PartId == part.Id)
                || (i.Template != null && i.Template.PartId == part.Id)
                || (i.LotNumber != null && db.LotRecords.Any(l => l.PartId == part.Id && l.LotNumber == i.LotNumber))
                || db.ReceivingRecords.Any(r => r.QcInspectionId == i.Id && r.PurchaseOrderLine.PartId == part.Id))
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Take(RecentInspectionLimit)
            .Select(i => new PartQualityInspectionModel(
                i.Id,
                i.Status,
                i.Template != null ? i.Template.Name : null,
                i.Job != null ? i.Job.JobNumber : null,
                i.LotNumber,
                db.Users.Where(u => u.Id == i.InspectorId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() ?? "",
                i.CreatedAt,
                i.CompletedAt,
                i.Results.Count(r => r.Passed == true),
                i.Results.Count(r => r.Passed == false)))
            .ToListAsync(cancellationToken);

        var openNcrs = await db.NonConformances
            .AsNoTracking()
            .Where(n => n.PartId == part.Id && n.Status != NcrStatus.Closed)
            .OrderByDescending(n => n.DetectedAt)
            .Select(n => new PartQualityNcrModel(
                n.Id,
                n.NcrNumber,
                n.Type.ToString(),
                n.Status.ToString(),
                n.LotNumber,
                n.DetectedAt,
                n.Description,
                n.AffectedQuantity))
            .ToListAsync(cancellationToken);

        var stockByLot = await db.BinContents
            .AsNoTracking()
            .Where(bc => bc.EntityType == "part" && bc.EntityId == part.Id
                && bc.RemovedAt == null && bc.LotNumber != null)
            .GroupBy(bc => bc.LotNumber!)
            .Select(g => new
            {
                LotNumber = g.Key,
                OnHand = g.Sum(bc => bc.Quantity),
                Held = g.Where(bc => bc.Status == BinContentStatus.QcHold).Sum(bc => bc.Quantity),
            })
            .ToListAsync(cancellationToken);
        var stockLotNumbers = stockByLot.Where(s => s.OnHand > 0).Select(s => s.LotNumber).ToList();

        var lotRows = await db.LotRecords
            .AsNoTracking()
            .Where(l => l.PartId == part.Id && stockLotNumbers.Contains(l.LotNumber))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new { l.Id, l.LotNumber, l.ExpirationDate, l.CreatedAt })
            .ToListAsync(cancellationToken);
        var stockLookup = stockByLot.ToDictionary(s => s.LotNumber);
        var lots = lotRows
            .Select(l =>
            {
                var stock = stockLookup[l.LotNumber];
                return new PartQualityLotModel(
                    l.Id, l.LotNumber, stock.OnHand, stock.Held, stock.Held > 0, l.ExpirationDate, l.CreatedAt);
            })
            .ToList();

        var templates = await db.QcChecklistTemplates
            .AsNoTracking()
            .Where(t => t.PartId == part.Id || t.Id == part.ReceivingInspectionTemplateId)
            .OrderBy(t => t.Name)
            .Select(t => new PartQualityTemplateModel(
                t.Id,
                t.Name,
                t.IsActive,
                t.Id == part.ReceivingInspectionTemplateId))
            .ToListAsync(cancellationToken);

        var spcCharacteristicCount = await db.SpcCharacteristics
            .AsNoTracking()
            .CountAsync(c => c.PartId == part.Id && c.IsActive, cancellationToken);

        return new PartQualitySummaryResponseModel(
            part.Id, recentInspections, openNcrs, lots, templates, spcCharacteristicCount);
    }
}
