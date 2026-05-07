using MediatR;
using Microsoft.EntityFrameworkCore;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.WorkingCalendars;

public record GetWorkingCalendarQuery(int Id) : IRequest<WorkingCalendarResponseModel>;

public class GetWorkingCalendarHandler(AppDbContext db, IShiftService shiftService)
    : IRequestHandler<GetWorkingCalendarQuery, WorkingCalendarResponseModel>
{
    public async Task<WorkingCalendarResponseModel> Handle(GetWorkingCalendarQuery request, CancellationToken ct)
    {
        var c = await db.WorkingCalendars
            .Include(x => x.Holidays)
            .Include(x => x.Shifts)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"WorkingCalendar {request.Id} not found.");

        var weeklyCapacity = await shiftService.GetWeeklyCapacityHoursAsync(c.Id, ct);
        var shifts = c.Shifts
            .OrderBy(s => s.StartTime)
            .Select(s => new CalendarShiftResponseModel(
                s.Id,
                s.WorkingCalendarId ?? c.Id,
                s.Name,
                s.DaysOfWeekMask ?? 0,
                s.StartTime,
                s.EndTime,
                s.PremiumMultiplier,
                s.CapacityHours,
                EffectiveCapacityHours(s.CapacityHours, s.NetHours, s.StartTime, s.EndTime),
                s.IsActive))
            .ToList();

        return new WorkingCalendarResponseModel(
            c.Id, c.Name, c.TimeZone, c.WorkingDaysMask, c.IsDefault, c.IsActive,
            c.Holidays.OrderBy(h => h.Date).Select(h => new HolidayResponseModel(
                h.Id, h.Date, h.Name, h.ObservedDate, h.IsRecurring)).ToList(),
            c.CreatedAt, c.UpdatedAt,
            shifts,
            weeklyCapacity);
    }

    private static decimal EffectiveCapacityHours(decimal capacityHours, decimal netHours, TimeOnly start, TimeOnly end)
    {
        if (capacityHours > 0) return capacityHours;
        if (netHours > 0) return netHours;
        var span = end <= start
            ? (TimeSpan.FromHours(24) - (start - TimeOnly.MinValue) + (end - TimeOnly.MinValue))
            : (end - start);
        return (decimal)span.TotalHours;
    }
}
