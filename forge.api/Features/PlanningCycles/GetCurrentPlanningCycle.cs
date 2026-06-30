using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PlanningCycles;

public record GetCurrentPlanningCycleQuery : IRequest<PlanningCycleDetailResponseModel?>;

public class GetCurrentPlanningCycleHandler(IPlanningCycleRepository repo, AppDbContext db)
    : IRequestHandler<GetCurrentPlanningCycleQuery, PlanningCycleDetailResponseModel?>
{
    public async Task<PlanningCycleDetailResponseModel?> Handle(GetCurrentPlanningCycleQuery request, CancellationToken cancellationToken)
    {
        var cycle = await repo.GetCurrentAsync(cancellationToken);
        if (cycle == null) return null;

        // Resolve assignee names in one query (no N+1) — key by user id.
        var assigneeIds = cycle.Entries
            .Where(e => e.Job.AssigneeId.HasValue)
            .Select(e => e.Job.AssigneeId!.Value)
            .Distinct()
            .ToList();

        var assigneeNames = assigneeIds.Count > 0
            ? await db.Users
                .Where(u => assigneeIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken)
            : [];

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
                e.Job.AssigneeId.HasValue && assigneeNames.TryGetValue(e.Job.AssigneeId.Value, out var name) ? name : null,
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
