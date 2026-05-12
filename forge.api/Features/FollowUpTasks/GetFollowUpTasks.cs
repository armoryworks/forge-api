using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.FollowUpTasks;

public record GetFollowUpTasksQuery(int UserId, FollowUpStatus? Status) : IRequest<List<FollowUpTaskResponseModel>>;

public record FollowUpTaskResponseModel(
    int Id,
    string Title,
    string? Description,
    int AssignedToUserId,
    string AssignedToName,
    DateTimeOffset? DueDate,
    string? SourceEntityType,
    int? SourceEntityId,
    FollowUpTriggerType TriggerType,
    FollowUpStatus Status,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? DismissedAt,
    DateTimeOffset CreatedAt);

public class GetFollowUpTasksHandler(AppDbContext db) : IRequestHandler<GetFollowUpTasksQuery, List<FollowUpTaskResponseModel>>
{
    public async Task<List<FollowUpTaskResponseModel>> Handle(GetFollowUpTasksQuery request, CancellationToken ct)
    {
        var query = db.FollowUpTasks
            .Where(f => f.AssignedToUserId == request.UserId);

        if (request.Status.HasValue)
            query = query.Where(f => f.Status == request.Status.Value);

        var tasks = await query
            .Join(db.Users, f => f.AssignedToUserId, u => u.Id, (f, u) => new { Task = f, User = u })
            .OrderBy(x => x.Task.DueDate)
            .ThenByDescending(x => x.Task.CreatedAt)
            .Select(x => new FollowUpTaskResponseModel(
                x.Task.Id,
                x.Task.Title,
                x.Task.Description,
                x.Task.AssignedToUserId,
                x.User.LastName + ", " + x.User.FirstName,
                x.Task.DueDate,
                x.Task.SourceEntityType,
                x.Task.SourceEntityId,
                x.Task.TriggerType,
                x.Task.Status,
                x.Task.CompletedAt,
                x.Task.DismissedAt,
                x.Task.CreatedAt))
            .ToListAsync(ct);

        return tasks;
    }
}
