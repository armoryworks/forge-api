using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models.Costing;
using Forge.Data.Context;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Lists overhead pools, optionally filtered by cost center.</summary>
public record ListOverheadPoolsQuery(int? CostingCostCenterId = null) : IRequest<List<OverheadPoolResponseModel>>;

public class ListOverheadPoolsHandler(AppDbContext db)
    : IRequestHandler<ListOverheadPoolsQuery, List<OverheadPoolResponseModel>>
{
    public async Task<List<OverheadPoolResponseModel>> Handle(ListOverheadPoolsQuery request, CancellationToken ct)
        => await db.OverheadCostPools.AsNoTracking()
            .Where(p => request.CostingCostCenterId == null || p.CostingCostCenterId == request.CostingCostCenterId)
            .OrderBy(p => p.Code)
            .Select(p => new OverheadPoolResponseModel(
                p.Id, p.CostingCostCenterId, p.WorkCenterId, p.Code, p.Name,
                p.Behavior.ToString(), p.FixedPortion, p.Driver.ToString()))
            .ToListAsync(ct);
}
