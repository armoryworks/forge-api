using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PlanningCycles;

public record ActivatePlanningCycleCommand(int Id) : IRequest;

public class ActivatePlanningCycleHandler(IPlanningCycleRepository repo)
    : IRequestHandler<ActivatePlanningCycleCommand>
{
    public async Task Handle(ActivatePlanningCycleCommand request, CancellationToken cancellationToken)
    {
        var cycle = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Planning cycle {request.Id} not found");

        if (cycle.Status != PlanningCycleStatus.Planning)
            throw new InvalidOperationException($"Cannot activate a cycle with status '{cycle.Status}'");

        cycle.Status = PlanningCycleStatus.Active;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
