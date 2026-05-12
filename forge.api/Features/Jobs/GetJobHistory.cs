using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetJobHistoryQuery(int JobId) : IRequest<List<ActivityResponseModel>>;

public class GetJobHistoryHandler(AppDbContext db) : IRequestHandler<GetJobHistoryQuery, List<ActivityResponseModel>>
{
    private static readonly HashSet<ActivityAction> HistoryActions =
    [
        ActivityAction.Created,
        ActivityAction.FieldChanged,
        ActivityAction.StageMoved,
        ActivityAction.StatusChanged,
        ActivityAction.Assigned,
        ActivityAction.Unassigned,
        ActivityAction.Archived,
        ActivityAction.Restored,
    ];

    public async Task<List<ActivityResponseModel>> Handle(GetJobHistoryQuery request, CancellationToken cancellationToken)
    {
        var logs = await db.JobActivityLogs
            .Where(l => l.JobId == request.JobId && HistoryActions.Contains(l.Action))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        var userIds = logs
            .Where(l => l.UserId.HasValue)
            .Select(l => l.UserId!.Value)
            .Distinct()
            .ToList();

        var users = userIds.Count > 0
            ? await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, cancellationToken)
            : new Dictionary<int, ApplicationUser>();

        return logs.Select(l =>
        {
            var user = l.UserId.HasValue && users.TryGetValue(l.UserId.Value, out var u) ? u : null;
            return new ActivityResponseModel(
                l.Id,
                l.Action.ToString(),
                l.FieldName,
                l.OldValue,
                l.NewValue,
                l.Description,
                user?.Initials,
                user is not null ? $"{user.LastName}, {user.FirstName}".Trim(',', ' ') : null,
                l.CreatedAt);
        }).ToList();
    }
}
