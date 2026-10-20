using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Services;

public static class WorkCenterCapacityLookup
{
    public static IQueryable<WorkCenter> WithCapacityData(this IQueryable<WorkCenter> workCenters) =>
        workCenters
            .Include(w => w.Shifts).ThenInclude(ws => ws.Shift)
            .Include(w => w.CalendarOverrides)
            .Include(w => w.Location);

    public static async Task<Dictionary<int, WorkCenterCapacity>> BuildAsync(
        AppDbContext db, IReadOnlyCollection<WorkCenter> workCenters, CancellationToken ct)
    {
        var locationCalendarIds = workCenters
            .Select(w => w.Location?.WorkingCalendarId)
            .OfType<int>()
            .Distinct()
            .ToList();

        var calendars = await db.WorkingCalendars
            .AsNoTracking()
            .Include(c => c.Holidays)
            .Include(c => c.Shifts)
            .Where(c => locationCalendarIds.Contains(c.Id) || (c.IsDefault && c.IsActive))
            .ToListAsync(ct);

        var defaultCalendar = calendars.FirstOrDefault(c => c.IsDefault && c.IsActive);

        return workCenters.ToDictionary(w => w.Id, w =>
        {
            var calendarId = w.Location?.WorkingCalendarId;
            var calendar = calendarId is null
                ? defaultCalendar
                : calendars.FirstOrDefault(c => c.Id == calendarId) ?? defaultCalendar;

            return new WorkCenterCapacity(
                w.Shifts.Select(ws => new WorkCenterShiftInfo(ShiftHours.EffectiveCapacity(ws.Shift), ws.DaysOfWeek)).ToList(),
                w.CalendarOverrides.ToDictionary(c => c.Date, c => c.AvailableHours),
                w.DailyCapacityHours,
                calendar?.WorkingDaysMask ?? WorkCenterCapacity.MondayToFridayMask,
                calendar?.Holidays.Select(h => h.ObservedDate ?? h.Date).ToHashSet() ?? [],
                w.EfficiencyPercent / 100m,
                w.NumberOfMachines)
            {
                TimeZone = FindTimeZone(calendar?.TimeZone),
                DayStart = DayStartOf(w.Shifts.Select(ws => ws.Shift))
                    ?? DayStartOf(calendar?.Shifts ?? [])
                    ?? WorkCenterCapacity.DefaultDayStart,
            };
        });
    }

    private static TimeOnly? DayStartOf(IEnumerable<Shift> shifts) =>
        shifts
            .Where(s => s.IsActive && s.EndTime > s.StartTime)
            .Select(s => (TimeOnly?)s.StartTime)
            .Min();

    private static TimeZoneInfo FindTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
