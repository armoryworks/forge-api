using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PlanningCycles;

public record CommitJobToCycleCommand(int CycleId, int JobId) : IRequest;

public class CommitJobToCycleHandler(IPlanningCycleRepository cycleRepo, IJobRepository jobRepo)
    : IRequestHandler<CommitJobToCycleCommand>
{
    public async Task Handle(CommitJobToCycleCommand request, CancellationToken cancellationToken)
    {
        var cycle = await cycleRepo.FindAsync(request.CycleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Planning cycle {request.CycleId} not found");

        var job = await jobRepo.FindAsync(request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        var existing = await cycleRepo.FindEntryAsync(request.CycleId, request.JobId, cancellationToken);
        if (existing != null)
            throw new InvalidOperationException("Job is already committed to this cycle");

        var maxSort = cycle.Entries.Any() ? cycle.Entries.Max(e => e.SortOrder) + 1 : 0;

        await cycleRepo.AddEntryAsync(new PlanningCycleEntry
        {
            PlanningCycleId = request.CycleId,
            JobId = request.JobId,
            CommittedAt = DateTimeOffset.UtcNow,
            SortOrder = maxSort,
        }, cancellationToken);

        await cycleRepo.SaveChangesAsync(cancellationToken);
    }
}
