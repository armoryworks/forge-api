using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Calendar;

/// <summary>
/// compliance-calendar A-3: the overlay calendar's layer list — Super-Groups (with their
/// Event-Types) the current user may see (A-2 visibility applied).
/// </summary>
public record GetCalendarSuperGroupsQuery : IRequest<List<CalendarSuperGroupResponseModel>>;

public class GetCalendarSuperGroupsHandler(AppDbContext db, ICalendarVisibilityService visibility)
    : IRequestHandler<GetCalendarSuperGroupsQuery, List<CalendarSuperGroupResponseModel>>
{
    public async Task<List<CalendarSuperGroupResponseModel>> Handle(
        GetCalendarSuperGroupsQuery request, CancellationToken cancellationToken)
    {
        var visibleGroupIds = await visibility.GetVisibleSuperGroupIdsAsync(cancellationToken);

        var query = db.CalendarSuperGroups.AsNoTracking().AsQueryable();
        if (visibleGroupIds is not null)
            query = query.Where(g => visibleGroupIds.Contains(g.Id));

        return await query
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .Select(g => new CalendarSuperGroupResponseModel(
                g.Id, g.Key, g.Name, g.Color, g.IconName, g.DefaultVisible, g.RequiresTracking, g.SortOrder,
                g.EventTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
                    .Select(t => new CalendarEventTypeResponseModel(
                        t.Id, t.SuperGroupId, t.Key, t.Name, t.Color, t.RequiresTracking, t.SortOrder))
                    .ToList()))
            .ToListAsync(cancellationToken);
    }
}
