using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetNcrsQuery(
    NcrType? Type,
    NcrStatus? Status,
    int? PartId,
    int? JobId,
    int? VendorId,
    int? CustomerId,
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo
) : IRequest<List<NcrResponseModel>>;

public class GetNcrsHandler(AppDbContext db)
    : IRequestHandler<GetNcrsQuery, List<NcrResponseModel>>
{
    public async Task<List<NcrResponseModel>> Handle(
        GetNcrsQuery request, CancellationToken cancellationToken)
    {
        var query = db.NonConformances
            .AsNoTracking()
            .Include(n => n.Part)
            .Include(n => n.Job)
            .Include(n => n.Capa)
            .Include(n => n.Customer)
            .Include(n => n.Vendor)
            .AsQueryable();

        if (request.Type.HasValue)
            query = query.Where(n => n.Type == request.Type.Value);
        if (request.Status.HasValue)
            query = query.Where(n => n.Status == request.Status.Value);
        if (request.PartId.HasValue)
            query = query.Where(n => n.PartId == request.PartId.Value);
        if (request.JobId.HasValue)
            query = query.Where(n => n.JobId == request.JobId.Value);
        if (request.VendorId.HasValue)
            query = query.Where(n => n.VendorId == request.VendorId.Value);
        if (request.CustomerId.HasValue)
            query = query.Where(n => n.CustomerId == request.CustomerId.Value);
        if (request.DateFrom.HasValue)
            query = query.Where(n => n.DetectedAt >= request.DateFrom.Value);
        if (request.DateTo.HasValue)
            query = query.Where(n => n.DetectedAt <= request.DateTo.Value);

        var ncrs = await query
            .OrderByDescending(n => n.DetectedAt)
            .ToListAsync(cancellationToken);

        // Pre-fetch user names for all user FKs
        var userIds = ncrs
            .SelectMany(n => new[] { n.DetectedById, n.ContainmentById ?? 0, n.DispositionById ?? 0 })
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken);

        return ncrs.Select(n => new NcrResponseModel
        {
            Id = n.Id,
            NcrNumber = n.NcrNumber,
            Type = n.Type,
            PartId = n.PartId,
            PartNumber = n.Part.PartNumber,
            PartDescription = n.Part.Description ?? string.Empty,
            JobId = n.JobId,
            JobNumber = n.Job?.JobNumber,
            ProductionRunId = n.ProductionRunId,
            LotNumber = n.LotNumber,
            SalesOrderLineId = n.SalesOrderLineId,
            PurchaseOrderLineId = n.PurchaseOrderLineId,
            QcInspectionId = n.QcInspectionId,
            DetectedById = n.DetectedById,
            DetectedByName = userNames.GetValueOrDefault(n.DetectedById, "Unknown"),
            DetectedAt = n.DetectedAt,
            DetectedAtStage = n.DetectedAtStage,
            Description = n.Description,
            AffectedQuantity = n.AffectedQuantity,
            DefectiveQuantity = n.DefectiveQuantity,
            ContainmentActions = n.ContainmentActions,
            ContainmentById = n.ContainmentById,
            ContainmentByName = n.ContainmentById.HasValue ? userNames.GetValueOrDefault(n.ContainmentById.Value) : null,
            ContainmentAt = n.ContainmentAt,
            DispositionCode = n.DispositionCode,
            DispositionById = n.DispositionById,
            DispositionByName = n.DispositionById.HasValue ? userNames.GetValueOrDefault(n.DispositionById.Value) : null,
            DispositionAt = n.DispositionAt,
            DispositionNotes = n.DispositionNotes,
            ReworkInstructions = n.ReworkInstructions,
            MaterialCost = n.MaterialCost,
            LaborCost = n.LaborCost,
            TotalCostImpact = n.TotalCostImpact,
            Status = n.Status,
            CapaId = n.CapaId,
            CapaNumber = n.Capa?.CapaNumber,
            CustomerId = n.CustomerId,
            CustomerName = n.Customer?.Name,
            VendorId = n.VendorId,
            VendorName = n.Vendor?.CompanyName,
            CreatedAt = n.CreatedAt,
        }).ToList();
    }
}
