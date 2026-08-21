using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models.Costing;
using Forge.Data.Context;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Lists costing periods, newest first.</summary>
public record ListCostingPeriodsQuery : IRequest<List<CostingPeriodResponseModel>>;

public class ListCostingPeriodsHandler(AppDbContext db)
    : IRequestHandler<ListCostingPeriodsQuery, List<CostingPeriodResponseModel>>
{
    public async Task<List<CostingPeriodResponseModel>> Handle(ListCostingPeriodsQuery request, CancellationToken ct)
        => await db.CostingPeriods.AsNoTracking()
            .OrderByDescending(p => p.StartDate)
            .Select(p => new CostingPeriodResponseModel(
                p.Id, p.StartDate, p.EndDate, p.Status.ToString(), p.FrozenAt, p.ClosedAt))
            .ToListAsync(ct);
}
