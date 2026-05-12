using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ScheduledTasks;

public record GetScheduledTasksQuery() : IRequest<List<ScheduledTaskResponseModel>>;

public class GetScheduledTasksHandler(AppDbContext db) : IRequestHandler<GetScheduledTasksQuery, List<ScheduledTaskResponseModel>>
{
    public async Task<List<ScheduledTaskResponseModel>> Handle(GetScheduledTasksQuery request, CancellationToken ct)
    {
        return await db.ScheduledTasks
            .Include(t => t.TrackType)
            .OrderBy(t => t.Name)
            .Select(t => new ScheduledTaskResponseModel(
                t.Id, t.Name, t.Description, t.TrackTypeId, t.TrackType.Name,
                t.InternalProjectTypeId, t.AssigneeId, t.CronExpression,
                t.IsActive, t.LastRunAt, t.NextRunAt, t.CreatedAt))
            .ToListAsync(ct);
    }
}
