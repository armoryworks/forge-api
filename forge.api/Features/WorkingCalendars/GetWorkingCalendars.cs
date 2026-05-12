using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.WorkingCalendars;

public record GetWorkingCalendarsQuery : IRequest<List<WorkingCalendarResponseModel>>;

public class GetWorkingCalendarsHandler(AppDbContext db)
    : IRequestHandler<GetWorkingCalendarsQuery, List<WorkingCalendarResponseModel>>
{
    public async Task<List<WorkingCalendarResponseModel>> Handle(GetWorkingCalendarsQuery request, CancellationToken ct)
    {
        var calendars = await db.WorkingCalendars
            .Include(c => c.Holidays)
            .AsNoTracking()
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        return calendars.Select(c => new WorkingCalendarResponseModel(
            c.Id, c.Name, c.TimeZone, c.WorkingDaysMask, c.IsDefault, c.IsActive,
            c.Holidays.OrderBy(h => h.Date).Select(h => new HolidayResponseModel(
                h.Id, h.Date, h.Name, h.ObservedDate, h.IsRecurring)).ToList(),
            c.CreatedAt, c.UpdatedAt
        )).ToList();
    }
}
