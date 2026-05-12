using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Default <see cref="IShiftService"/>. Reads calendar-bound
/// (WorkingCalendarId-set) <c>Shift</c> rows and resolves the two
/// helper questions: per-week capacity hours and within-shift checks.
///
/// <para>Shifts without a calendar binding (work-center templates) are
/// ignored entirely — they participate in scheduling via the
/// <c>WorkCenterShift</c> junction, not in plant-level hours-of-operation.</para>
/// </summary>
public class ShiftService(AppDbContext db) : IShiftService
{
    public async Task<decimal> GetWeeklyCapacityHoursAsync(int workingCalendarId, CancellationToken ct)
    {
        var shifts = await db.Shifts
            .AsNoTracking()
            .Where(s => s.WorkingCalendarId == workingCalendarId && s.IsActive)
            .Select(s => new { s.DaysOfWeekMask, s.CapacityHours, s.NetHours, s.StartTime, s.EndTime })
            .ToListAsync(ct);

        decimal total = 0m;
        foreach (var s in shifts)
        {
            var occurrencesPerWeek = CountBits(s.DaysOfWeekMask ?? 0);
            if (occurrencesPerWeek == 0) continue;

            // Hours per occurrence: explicit CapacityHours wins; fall back to
            // NetHours; final fallback to wall-clock duration.
            var hoursPerOccurrence = s.CapacityHours > 0
                ? s.CapacityHours
                : s.NetHours > 0
                    ? s.NetHours
                    : WallClockHours(s.StartTime, s.EndTime);
            total += hoursPerOccurrence * occurrencesPerWeek;
        }
        return total;
    }

    public async Task<bool> IsWithinShiftAsync(int workingCalendarId, DateTimeOffset moment, CancellationToken ct)
    {
        var calendar = await db.WorkingCalendars
            .AsNoTracking()
            .Where(c => c.Id == workingCalendarId)
            .Select(c => new { c.TimeZone })
            .FirstOrDefaultAsync(ct);
        if (calendar == null) return false;

        // Resolve the moment in the calendar's time zone so day-of-week +
        // wall-clock comparisons match operator expectations.
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZone);
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }
        var local = TimeZoneInfo.ConvertTime(moment, tz);
        var dow = (int)local.DayOfWeek; // Sunday = 0
        var dayBit = 1 << dow;
        var localTime = TimeOnly.FromTimeSpan(local.TimeOfDay);

        var shifts = await db.Shifts
            .AsNoTracking()
            .Where(s => s.WorkingCalendarId == workingCalendarId && s.IsActive)
            .Select(s => new { s.DaysOfWeekMask, s.StartTime, s.EndTime })
            .ToListAsync(ct);

        foreach (var s in shifts)
        {
            var mask = s.DaysOfWeekMask ?? 0;
            var spansMidnight = s.EndTime <= s.StartTime;
            if (spansMidnight)
            {
                // Graveyard: covers (start..24:00) on day N AND (00:00..end) on day N+1.
                // The shift "owns" day N (its start day); membership is true on day N
                // when localTime >= start, OR on day N+1 when localTime < end.
                var prevDayBit = 1 << ((dow + 6) % 7);
                if ((mask & dayBit) != 0 && localTime >= s.StartTime) return true;
                if ((mask & prevDayBit) != 0 && localTime < s.EndTime) return true;
            }
            else
            {
                if ((mask & dayBit) == 0) continue;
                if (localTime >= s.StartTime && localTime < s.EndTime) return true;
            }
        }
        return false;
    }

    private static int CountBits(int mask)
    {
        // Restrict to 7 days of week. Higher bits are noise.
        var n = mask & 0x7F;
        var count = 0;
        while (n != 0)
        {
            count += n & 1;
            n >>= 1;
        }
        return count;
    }

    private static decimal WallClockHours(TimeOnly start, TimeOnly end)
    {
        var span = end <= start
            ? (TimeSpan.FromHours(24) - (start - TimeOnly.MinValue) + (end - TimeOnly.MinValue))
            : (end - start);
        return (decimal)span.TotalHours;
    }
}
