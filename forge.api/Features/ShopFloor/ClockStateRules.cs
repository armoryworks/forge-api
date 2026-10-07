using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public static class ClockStateRules
{
    public const string StatusIn = "In";
    public const string StatusOut = "Out";
    public const int DefaultLookbackHours = 168;
    public const int OpenFromPriorShiftHours = 16;

    public static async Task<TimeZoneInfo> ShopTimeZoneAsync(AppDbContext db, CancellationToken ct = default)
    {
        var calendarZone = await db.WorkingCalendars
            .AsNoTracking()
            .Where(c => c.IsDefault)
            .Select(c => c.TimeZone)
            .FirstOrDefaultAsync(ct);
        if (TryFindTimeZone(calendarZone) is { } calendarTimeZone)
            return calendarTimeZone;

        var plantZone = await db.Plants
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Id)
            .Select(p => p.TimeZone)
            .FirstOrDefaultAsync(ct);
        if (TryFindTimeZone(plantZone) is { } plantTimeZone)
            return plantTimeZone;

        var bookZone = await db.Books
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => b.ReportingTimeZone)
            .FirstOrDefaultAsync(ct);

        return TryFindTimeZone(bookZone) ?? TimeZoneInfo.Utc;
    }

    private static TimeZoneInfo? TryFindTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return null;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<DateTimeOffset> ShopDayStartUtcAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct = default)
        => DayStartUtc(await ShopTimeZoneAsync(db, ct), now);

    public static DateTime LocalToday(TimeZoneInfo timeZone, DateTimeOffset now)
        => TimeZoneInfo.ConvertTime(now, timeZone).Date;

    public static DateTimeOffset DayStartUtc(TimeZoneInfo timeZone, DateTimeOffset now)
        => LocalMidnightUtc(timeZone, LocalToday(timeZone, now));

    public static DateTimeOffset LocalMidnightUtc(TimeZoneInfo timeZone, DateTime localDate)
    {
        var midnight = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, timeZone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    public static bool IsOverdue(DateTimeOffset? dueDate, DateTime shopToday)
        => dueDate.HasValue && dueDate.Value.UtcDateTime.Date < shopToday.Date;

    public static async Task<Dictionary<int, ClockEvent>> LatestEventsAsync(
        AppDbContext db,
        IReadOnlyCollection<int>? userIds,
        DateTimeOffset now,
        int lookbackHours = DefaultLookbackHours,
        CancellationToken ct = default)
    {
        var since = now.AddHours(-lookbackHours);
        var query = db.ClockEvents.AsNoTracking().Where(e => e.Timestamp >= since);
        if (userIds is not null)
        {
            var ids = userIds.ToArray();
            query = query.Where(e => ids.Contains(e.UserId));
        }

        var events = await query.ToListAsync(ct);

        return events
            .GroupBy(e => e.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.Id).First());
    }

    public static Task<ClockEvent?> LatestEventAsync(AppDbContext db, int userId, CancellationToken ct = default)
        => db.ClockEvents
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync(ct);

    public static (string Status, bool CountsAsActive) ResolveStatus(
        ClockEvent? evt, IEnumerable<ClockEventTypeDefinition> definitions)
    {
        if (evt is null)
            return (StatusOut, false);

        var code = string.IsNullOrEmpty(evt.EventTypeCode) ? evt.EventType.ToString() : evt.EventTypeCode;
        var definition = definitions.FirstOrDefault(d => d.Code == code);

        return definition is null
            ? (StatusOut, false)
            : (definition.StatusMapping, definition.CountsAsActive);
    }

    public static async Task<(string Status, bool CountsAsActive)> ResolveStatusAsync(
        ClockEvent? evt, IClockEventTypeService clockEventTypeService, CancellationToken ct = default)
        => ResolveStatus(evt, await clockEventTypeService.GetAllAsync(ct));

    public static string ToPhoneState(string status, bool countsAsActive)
        => status == StatusIn ? "in"
            : status != StatusOut && countsAsActive ? "break"
            : "out";

    public static bool IsOpenFromPriorShift(
        ClockEvent? evt, bool countsAsActive, DateTimeOffset dayStartUtc, DateTimeOffset now)
        => evt is not null
            && countsAsActive
            && evt.Timestamp < dayStartUtc
            && now - evt.Timestamp >= TimeSpan.FromHours(OpenFromPriorShiftHours);
}
