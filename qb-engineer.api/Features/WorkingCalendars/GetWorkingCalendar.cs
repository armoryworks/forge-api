using MediatR;
using Microsoft.EntityFrameworkCore;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.WorkingCalendars;

public record GetWorkingCalendarQuery(int Id) : IRequest<WorkingCalendarResponseModel>;

public class GetWorkingCalendarHandler(AppDbContext db)
    : IRequestHandler<GetWorkingCalendarQuery, WorkingCalendarResponseModel>
{
    public async Task<WorkingCalendarResponseModel> Handle(GetWorkingCalendarQuery request, CancellationToken ct)
    {
        var c = await db.WorkingCalendars
            .Include(x => x.Holidays)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"WorkingCalendar {request.Id} not found.");

        return new WorkingCalendarResponseModel(
            c.Id, c.Name, c.TimeZone, c.WorkingDaysMask, c.IsDefault, c.IsActive,
            c.Holidays.OrderBy(h => h.Date).Select(h => new HolidayResponseModel(
                h.Id, h.Date, h.Name, h.ObservedDate, h.IsRecurring)).ToList(),
            c.CreatedAt, c.UpdatedAt);
    }
}
