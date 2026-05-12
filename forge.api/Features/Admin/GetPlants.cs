using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetPlantsQuery : IRequest<List<PlantResponseModel>>;

public class GetPlantsHandler(AppDbContext db) : IRequestHandler<GetPlantsQuery, List<PlantResponseModel>>
{
    public async Task<List<PlantResponseModel>> Handle(GetPlantsQuery request, CancellationToken cancellationToken)
    {
        var plants = await db.Plants
            .AsNoTracking()
            .Include(p => p.Location)
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);

        return plants.Select(p => new PlantResponseModel(
            p.Id, p.Code, p.Name, p.CompanyLocationId,
            p.Location.Name, p.TimeZone, p.CurrencyCode,
            p.IsActive, p.IsDefault,
            p.CreatedAt, p.UpdatedAt)).ToList();
    }
}
