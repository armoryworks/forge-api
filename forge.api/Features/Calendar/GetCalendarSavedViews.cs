using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Calendar;

/// <summary>
/// compliance-calendar A-3: the current user's saved views — their personal views plus any
/// role-default views for their roles. Optionally scoped (e.g. "module:compliance").
/// </summary>
public record GetCalendarSavedViewsQuery(string? Scope) : IRequest<List<CalendarSavedViewResponseModel>>;

public class GetCalendarSavedViewsHandler(AppDbContext db)
    : IRequestHandler<GetCalendarSavedViewsQuery, List<CalendarSavedViewResponseModel>>
{
    public async Task<List<CalendarSavedViewResponseModel>> Handle(
        GetCalendarSavedViewsQuery request, CancellationToken cancellationToken)
    {
        var userId = db.CurrentUserId;

        var roles = userId is null
            ? []
            : await db.UserRoles
                .Where(ur => ur.UserId == userId.Value)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                .ToListAsync(cancellationToken);

        var query = db.CalendarSavedViews.AsNoTracking()
            .Where(v => v.OwnerUserId == userId
                || (v.OwnerUserId == null && v.RoleKey != null && roles.Contains(v.RoleKey)));

        if (!string.IsNullOrEmpty(request.Scope))
            query = query.Where(v => v.Scope == request.Scope);

        return await query
            .OrderBy(v => v.Name)
            .Select(v => new CalendarSavedViewResponseModel(
                v.Id, v.Name, v.OwnerUserId, v.RoleKey, v.Scope,
                v.SelectedSuperGroupIds, v.SelectedEventTypeIds, v.IsDefault))
            .ToListAsync(cancellationToken);
    }
}
