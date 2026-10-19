using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Hours worked per person per shop-local day, for overtime and the piece-rate
/// minimum-wage check. A day counts the larger of clocked attendance (time spent in an
/// "In" clock status, each stretch capped at <see cref="ClockStateRules.OpenFromPriorShiftHours"/>
/// and clipped to now) and the time entries dated that day (manual, stopped or running).
/// </summary>
public class WorkedHoursService(AppDbContext db, IClockEventTypeService clockEventTypes, IClock clock)
    : IWorkedHoursService
{
    public async Task<Dictionary<int, Dictionary<DateOnly, decimal>>> DailyHoursAsync(
        IReadOnlyCollection<int> userIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var result = userIds.Distinct().ToDictionary(id => id, _ => new Dictionary<DateOnly, decimal>());
        if (result.Count == 0 || to < from)
            return result;

        var ids = result.Keys.ToArray();
        var now = clock.UtcNow;
        var zone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        var windowStart = ClockStateRules.LocalMidnightUtc(zone, from.ToDateTime(TimeOnly.MinValue));
        var windowEnd = ClockStateRules.LocalMidnightUtc(zone, to.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var entryMinutes = new Dictionary<(int, DateOnly), decimal>();
        var entries = await db.TimeEntries.AsNoTracking()
            .Where(t => ids.Contains(t.UserId) && t.Date >= from && t.Date <= to)
            .Select(t => new { t.UserId, t.Date, t.TimerStart, t.TimerStop, t.DurationMinutes })
            .ToListAsync(ct);
        foreach (var t in entries)
        {
            var key = (t.UserId, t.Date);
            entryMinutes[key] = entryMinutes.GetValueOrDefault(key)
                + OperationTimeMath.EntryMinutes(t.TimerStart, t.TimerStop, t.DurationMinutes, now);
        }

        var clockedMinutes = new Dictionary<(int, DateOnly), decimal>();
        var maxStretch = TimeSpan.FromHours(ClockStateRules.OpenFromPriorShiftHours);
        var lookback = windowStart - maxStretch;
        var events = await db.ClockEvents.AsNoTracking()
            .Where(e => ids.Contains(e.UserId) && e.Timestamp >= lookback && e.Timestamp < windowEnd)
            .ToListAsync(ct);
        if (events.Count > 0)
        {
            var definitions = await clockEventTypes.GetAllAsync(ct);
            var countUntil = now < windowEnd ? now : windowEnd;
            foreach (var group in events.GroupBy(e => e.UserId))
            {
                var ordered = group.OrderBy(e => e.Timestamp).ThenBy(e => e.Id).ToList();
                for (var i = 0; i < ordered.Count; i++)
                {
                    if (ClockStateRules.ResolveStatus(ordered[i], definitions).Status != ClockStateRules.StatusIn)
                        continue;

                    var start = ordered[i].Timestamp;
                    var end = i + 1 < ordered.Count ? ordered[i + 1].Timestamp : countUntil;
                    if (end > start + maxStretch) end = start + maxStretch;
                    if (end > countUntil) end = countUntil;
                    if (start < windowStart) start = windowStart;

                    AddByShopDay(clockedMinutes, group.Key, start, end, zone);
                }
            }
        }

        foreach (var key in entryMinutes.Keys.Union(clockedMinutes.Keys))
        {
            var minutes = Math.Max(entryMinutes.GetValueOrDefault(key), clockedMinutes.GetValueOrDefault(key));
            if (minutes > 0)
                result[key.Item1][key.Item2] = minutes / 60m;
        }

        return result;
    }

    private static void AddByShopDay(
        Dictionary<(int, DateOnly), decimal> minutesByDay, int userId,
        DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var cursor = start;
        while (cursor < end)
        {
            var localDate = ClockStateRules.LocalToday(zone, cursor);
            var dayEnd = ClockStateRules.LocalMidnightUtc(zone, localDate.AddDays(1));
            var segmentEnd = dayEnd < end ? dayEnd : end;
            var key = (userId, DateOnly.FromDateTime(localDate));
            minutesByDay[key] = minutesByDay.GetValueOrDefault(key) + (decimal)(segmentEnd - cursor).TotalMinutes;
            cursor = segmentEnd;
        }
    }
}
