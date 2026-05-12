using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PlanningCycles;

public record CompleteEntryCommand(int CycleId, int JobId) : IRequest;

public class CompleteEntryHandler(IPlanningCycleRepository repo)
    : IRequestHandler<CompleteEntryCommand>
{
    public async Task Handle(CompleteEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindEntryAsync(request.CycleId, request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entry not found for cycle {request.CycleId} and job {request.JobId}");

        entry.CompletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
