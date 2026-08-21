using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models.Costing;
using Forge.Data.Context;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Lists the frozen work-center cost rates for a costing period.</summary>
public record ListWorkCenterCostRatesQuery(int PeriodId) : IRequest<List<WorkCenterCostRateResponseModel>>;

public class ListWorkCenterCostRatesHandler(AppDbContext db)
    : IRequestHandler<ListWorkCenterCostRatesQuery, List<WorkCenterCostRateResponseModel>>
{
    public async Task<List<WorkCenterCostRateResponseModel>> Handle(ListWorkCenterCostRatesQuery request, CancellationToken ct)
        => await db.WorkCenterCostRates.AsNoTracking()
            .Where(r => r.CostingPeriodId == request.PeriodId)
            .OrderBy(r => r.WorkCenterId)
            .Select(r => new WorkCenterCostRateResponseModel(
                r.Id, r.WorkCenterId, r.CostingPeriodId, r.LaborRate, r.LaborOhRate,
                r.MachineRate, r.MachineOhVarRate, r.MachineOhFixedRate, r.FrozenAt, r.FrozenBy))
            .ToListAsync(ct);
}
