using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PlanningCycles;

public record GetPlanningCycleByIdQuery(int Id) : IRequest<PlanningCycleDetailResponseModel>;

public class GetPlanningCycleByIdHandler(IPlanningCycleRepository repo)
    : IRequestHandler<GetPlanningCycleByIdQuery, PlanningCycleDetailResponseModel>
{
    public async Task<PlanningCycleDetailResponseModel> Handle(GetPlanningCycleByIdQuery request, CancellationToken cancellationToken)
    {
        var cycle = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Planning cycle {request.Id} not found");

        return new PlanningCycleDetailResponseModel(
            cycle.Id,
            cycle.Name,
            cycle.StartDate,
            cycle.EndDate,
            cycle.Goals,
            cycle.Status.ToString(),
            cycle.DurationDays,
            cycle.Entries.OrderBy(e => e.SortOrder).Select(e => new PlanningCycleEntryResponseModel(
                e.Id,
                e.JobId,
                e.Job.JobNumber,
                e.Job.Title,
                null,
                e.Job.CurrentStage.Name,
                e.Job.CurrentStage.Color,
                e.Job.Priority.ToString(),
                e.IsRolledOver,
                e.CommittedAt,
                e.CompletedAt,
                e.SortOrder)).ToList(),
            cycle.CreatedAt,
            cycle.UpdatedAt);
    }
}
