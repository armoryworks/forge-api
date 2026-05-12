using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Services;

/// <summary>
/// Default WorkingCalendarService implementation backed by AppDbContext.
/// Loads the calendar's holiday set per call (no in-memory caching in v1 —
/// holiday counts per calendar are small, < ~30 rows including recurring).
/// If profiling shows this is hot, an in-memory cache keyed by calendar id
/// + (year-of-date) is the next iteration.
/// </summary>
public class WorkingCalendarService(AppDbContext db) : IWorkingCalendarService
{
    public async Task<DateOnly> AddBusinessDaysAsync(DateOnly start, int n, int? calendarId = null, CancellationToken ct = default)
    {
        if (n == 0) return start;

        var resolvedId = await ResolveCalendarIdAsync(calendarId, ct);
        var calendar = await db.WorkingCalendars
            .Include(c => c.Holidays)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == resolvedId, ct)
            ?? throw new InvalidOperationException($"WorkingCalendar {resolvedId} not found.");

        var holidays = BuildHolidaySet(calendar.Holidays);
        var step = n > 0 ? 1 : -1;
        var remaining = Math.Abs(n);
        var current = start;

        while (remaining > 0)
        {
            current = current.AddDays(step);
            if (IsWorkingDay(current, calendar.WorkingDaysMask, holidays))
                remaining--;
        }

        return current;
    }

    public async Task<int> BusinessDaysBetweenAsync(DateOnly start, DateOnly end, int? calendarId = null, CancellationToken ct = default)
    {
        if (end <= start) return 0;

        var resolvedId = await ResolveCalendarIdAsync(calendarId, ct);
        var calendar = await db.WorkingCalendars
            .Include(c => c.Holidays)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == resolvedId, ct)
            ?? throw new InvalidOperationException($"WorkingCalendar {resolvedId} not found.");

        var holidays = BuildHolidaySet(calendar.Holidays);
        var count = 0;
        for (var d = start.AddDays(1); d < end; d = d.AddDays(1))
        {
            if (IsWorkingDay(d, calendar.WorkingDaysMask, holidays))
                count++;
        }
        return count;
    }

    public async Task<bool> IsWorkingDayAsync(DateOnly date, int? calendarId = null, CancellationToken ct = default)
    {
        var resolvedId = await ResolveCalendarIdAsync(calendarId, ct);
        var calendar = await db.WorkingCalendars
            .Include(c => c.Holidays)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == resolvedId, ct)
            ?? throw new InvalidOperationException($"WorkingCalendar {resolvedId} not found.");

        return IsWorkingDay(date, calendar.WorkingDaysMask, BuildHolidaySet(calendar.Holidays));
    }

    public async Task<DateOnly> NextWorkingDayAsync(DateOnly date, int? calendarId = null, CancellationToken ct = default)
    {
        var resolvedId = await ResolveCalendarIdAsync(calendarId, ct);
        var calendar = await db.WorkingCalendars
            .Include(c => c.Holidays)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == resolvedId, ct)
            ?? throw new InvalidOperationException($"WorkingCalendar {resolvedId} not found.");

        var holidays = BuildHolidaySet(calendar.Holidays);
        var current = date;
        while (!IsWorkingDay(current, calendar.WorkingDaysMask, holidays))
        {
            current = current.AddDays(1);
        }
        return current;
    }

    public async Task<int> ResolveCalendarIdAsync(int? calendarId = null, CancellationToken ct = default)
    {
        if (calendarId.HasValue) return calendarId.Value;

        var defaultId = await db.WorkingCalendars
            .AsNoTracking()
            .Where(c => c.IsDefault && c.IsActive)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);

        return defaultId
            ?? throw new InvalidOperationException(
                "No default WorkingCalendar configured. Create one in Admin → Working Calendars and mark it as default.");
    }

    /// <summary>
    /// Build a set of "non-working" dates from the calendar's holidays. Uses
    /// ObservedDate when set (handles "Independence Day on Saturday observed
    /// Friday"), else the literal Date.
    /// </summary>
    private static HashSet<DateOnly> BuildHolidaySet(IEnumerable<Core.Entities.Holiday> holidays)
    {
        var set = new HashSet<DateOnly>();
        foreach (var h in holidays)
        {
            set.Add(h.ObservedDate ?? h.Date);
        }
        return set;
    }

    /// <summary>
    /// Day-of-week + holiday check. WorkingDaysMask bit 0 = Sun, bit 1 = Mon,
    /// ..., bit 6 = Sat. DayOfWeek enum values match (Sun=0..Sat=6) so a
    /// direct bit-shift works.
    /// </summary>
    private static bool IsWorkingDay(DateOnly date, int workingDaysMask, HashSet<DateOnly> holidays)
    {
        var dowBit = 1 << (int)date.DayOfWeek;
        if ((workingDaysMask & dowBit) == 0) return false;
        if (holidays.Contains(date)) return false;
        return true;
    }
}
