using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetInterPlantTransfersQuery(
    InterPlantTransferStatus? Status = null,
    int? PlantId = null) : IRequest<List<InterPlantTransferResponseModel>>;

public class GetInterPlantTransfersHandler(AppDbContext db) : IRequestHandler<GetInterPlantTransfersQuery, List<InterPlantTransferResponseModel>>
{
    public async Task<List<InterPlantTransferResponseModel>> Handle(GetInterPlantTransfersQuery request, CancellationToken cancellationToken)
    {
        var query = db.InterPlantTransfers
            .AsNoTracking()
            .Include(t => t.FromPlant)
            .Include(t => t.ToPlant)
            .Include(t => t.Lines).ThenInclude(l => l.Part)
            .AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);

        if (request.PlantId.HasValue)
            query = query.Where(t => t.FromPlantId == request.PlantId.Value || t.ToPlantId == request.PlantId.Value);

        var transfers = await query
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        return transfers.Select(t => new InterPlantTransferResponseModel(
            t.Id, t.TransferNumber,
            t.FromPlantId, t.FromPlant.Name,
            t.ToPlantId, t.ToPlant.Name,
            t.Status, t.ShippedAt, t.ReceivedAt,
            t.TrackingNumber, t.Notes,
            t.Lines.Count,
            t.Lines.Select(l => new InterPlantTransferLineResponseModel(
                l.Id, l.PartId, l.Part.PartNumber, l.Part.Description ?? l.Part.Name,
                l.Quantity, l.ReceivedQuantity, l.LotNumber)).ToList(),
            t.CreatedAt, t.UpdatedAt)).ToList();
    }
}
