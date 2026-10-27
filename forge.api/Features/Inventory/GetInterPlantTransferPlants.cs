using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetInterPlantTransferPlantsQuery : IRequest<List<InterPlantTransferPlantResponseModel>>;

public class GetInterPlantTransferPlantsHandler(AppDbContext db)
    : IRequestHandler<GetInterPlantTransferPlantsQuery, List<InterPlantTransferPlantResponseModel>>
{
    public async Task<List<InterPlantTransferPlantResponseModel>> Handle(GetInterPlantTransferPlantsQuery request, CancellationToken cancellationToken)
        => await db.Plants
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.Code)
            .Select(p => new InterPlantTransferPlantResponseModel(p.Id, p.Code, p.Name, p.IsDefault))
            .ToListAsync(cancellationToken);
}
