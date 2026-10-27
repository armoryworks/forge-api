using MediatR;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetInterPlantTransfersQuery(
    InterPlantTransferStatus? Status = null,
    int? PlantId = null) : IRequest<List<InterPlantTransferResponseModel>>;

public class GetInterPlantTransfersHandler(AppDbContext db) : IRequestHandler<GetInterPlantTransfersQuery, List<InterPlantTransferResponseModel>>
{
    public Task<List<InterPlantTransferResponseModel>> Handle(GetInterPlantTransfersQuery request, CancellationToken cancellationToken)
    {
        var query = db.InterPlantTransfers.AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);

        if (request.PlantId.HasValue)
            query = query.Where(t => t.FromPlantId == request.PlantId.Value || t.ToPlantId == request.PlantId.Value);

        return InterPlantTransferResponses.LoadAsync(db, query, cancellationToken);
    }
}
