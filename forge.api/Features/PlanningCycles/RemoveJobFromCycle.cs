using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PlanningCycles;

public record RemoveJobFromCycleCommand(int CycleId, int JobId) : IRequest;

public class RemoveJobFromCycleHandler(IPlanningCycleRepository repo)
    : IRequestHandler<RemoveJobFromCycleCommand>
{
    public async Task Handle(RemoveJobFromCycleCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindEntryAsync(request.CycleId, request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entry not found for cycle {request.CycleId} and job {request.JobId}");

        repo.RemoveEntry(entry);
        await repo.SaveChangesAsync(cancellationToken);
    }
}
