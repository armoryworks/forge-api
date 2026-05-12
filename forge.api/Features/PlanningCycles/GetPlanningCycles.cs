using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PlanningCycles;

public record GetPlanningCyclesQuery : IRequest<List<PlanningCycleListItemModel>>;

public class GetPlanningCyclesHandler(IPlanningCycleRepository repo)
    : IRequestHandler<GetPlanningCyclesQuery, List<PlanningCycleListItemModel>>
{
    public async Task<List<PlanningCycleListItemModel>> Handle(GetPlanningCyclesQuery request, CancellationToken cancellationToken)
    {
        return await repo.GetAllAsync(cancellationToken);
    }
}
