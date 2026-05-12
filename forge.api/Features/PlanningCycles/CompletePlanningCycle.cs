using MediatR;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PlanningCycles;

public record CompletePlanningCycleCommand(int Id, bool RolloverIncomplete) : IRequest<int?>;

public class CompletePlanningCycleHandler(IPlanningCycleRepository repo)
    : IRequestHandler<CompletePlanningCycleCommand, int?>
{
    public async Task<int?> Handle(CompletePlanningCycleCommand request, CancellationToken cancellationToken)
    {
        var cycle = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Planning cycle {request.Id} not found");

        if (cycle.Status == PlanningCycleStatus.Completed)
            throw new InvalidOperationException("Cycle is already completed");

        cycle.Status = PlanningCycleStatus.Completed;

        int? newCycleId = null;

        if (request.RolloverIncomplete)
        {
            var incompleteEntries = cycle.Entries.Where(e => !e.CompletedAt.HasValue).ToList();
            if (incompleteEntries.Count > 0)
            {
                var nextStart = cycle.EndDate.AddDays(1);
                var newCycle = new PlanningCycle
                {
                    Name = $"Cycle starting {nextStart:MMM d}",
                    StartDate = nextStart,
                    EndDate = nextStart.AddDays(cycle.DurationDays),
                    DurationDays = cycle.DurationDays,
                    Status = PlanningCycleStatus.Planning,
                };

                await repo.AddAsync(newCycle, cancellationToken);
                await repo.SaveChangesAsync(cancellationToken);

                var sortOrder = 0;
                foreach (var entry in incompleteEntries.OrderBy(e => e.SortOrder))
                {
                    await repo.AddEntryAsync(new PlanningCycleEntry
                    {
                        PlanningCycleId = newCycle.Id,
                        JobId = entry.JobId,
                        CommittedAt = DateTimeOffset.UtcNow,
                        IsRolledOver = true,
                        SortOrder = sortOrder++,
                    }, cancellationToken);
                }

                await repo.SaveChangesAsync(cancellationToken);
                newCycleId = newCycle.Id;
            }
        }

        await repo.SaveChangesAsync(cancellationToken);
        return newCycleId;
    }
}
