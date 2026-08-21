using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models.Costing;
using Forge.Data.Context;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Lists costing cost centers.</summary>
public record ListCostingCostCentersQuery : IRequest<List<CostingCostCenterResponseModel>>;

public class ListCostingCostCentersHandler(AppDbContext db)
    : IRequestHandler<ListCostingCostCentersQuery, List<CostingCostCenterResponseModel>>
{
    public async Task<List<CostingCostCenterResponseModel>> Handle(ListCostingCostCentersQuery request, CancellationToken ct)
        => await db.CostingCostCenters.AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new CostingCostCenterResponseModel(
                c.Id, c.Code, c.Name, c.ParentId, c.Type.ToString(), c.Sqft, c.Headcount, c.IsInventoriable))
            .ToListAsync(ct);
}
