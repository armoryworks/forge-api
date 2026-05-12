using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetNcrByIdQuery(int Id) : IRequest<NcrResponseModel>;

public class GetNcrByIdHandler(AppDbContext db)
    : IRequestHandler<GetNcrByIdQuery, NcrResponseModel>
{
    public async Task<NcrResponseModel> Handle(
        GetNcrByIdQuery request, CancellationToken cancellationToken)
    {
        var n = await db.NonConformances
            .AsNoTracking()
            .Include(x => x.Part)
            .Include(x => x.Job)
            .Include(x => x.Capa)
            .Include(x => x.Customer)
            .Include(x => x.Vendor)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"NCR {request.Id} not found");

        var userIds = new[] { n.DetectedById, n.ContainmentById ?? 0, n.DispositionById ?? 0 }
            .Where(id => id > 0).Distinct().ToList();

        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken);

        return new NcrResponseModel
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
        };
    }
}
