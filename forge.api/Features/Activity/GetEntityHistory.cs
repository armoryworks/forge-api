using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

using Microsoft.EntityFrameworkCore;

namespace Forge.Api.Features.Activity;

public record GetEntityHistoryQuery(string EntityType, int EntityId) : IRequest<List<ActivityResponseModel>>;

public class GetEntityHistoryHandler(AppDbContext db)
    : IRequestHandler<GetEntityHistoryQuery, List<ActivityResponseModel>>
{
    public async Task<List<ActivityResponseModel>> Handle(GetEntityHistoryQuery request, CancellationToken ct)
    {
        var entries = await db.ActivityLogs
            .Where(a => a.EntityType == request.EntityType
                && a.EntityId == request.EntityId
                && a.Action != "Comment")
            .OrderByDescending(a => a.CreatedAt)
            .Join(db.Users,
                a => a.UserId,
                u => u.Id,
                (a, u) => new { Log = a, User = u })
            .ToListAsync(ct);

        // Also include system entries with no user
        var systemEntries = await db.ActivityLogs
            .Where(a => a.EntityType == request.EntityType
                && a.EntityId == request.EntityId
                && a.Action != "Comment"
                && a.UserId == null)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var userEntries = entries.Select(e => new ActivityResponseModel(
            e.Log.Id,
            e.Log.Action,
            e.Log.FieldName,
            e.Log.OldValue,
            e.Log.NewValue,
            e.Log.Description,
            e.User.Initials,
            $"{e.User.LastName}, {e.User.FirstName}".Trim(',', ' '),
            e.Log.CreatedAt));

        var sysEntries = systemEntries.Select(e => new ActivityResponseModel(
            e.Id,
            e.Action,
            e.FieldName,
            e.OldValue,
            e.NewValue,
            e.Description,
            null,
            null,
            e.CreatedAt));

        return userEntries.Concat(sysEntries)
            .DistinctBy(a => a.Id)
            .OrderByDescending(a => a.CreatedAt)
            .ToList();
    }
}
