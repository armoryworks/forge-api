using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Services;

public sealed record ShopCalendar(int WorkingDaysMask, IReadOnlySet<DateOnly> Holidays)
{
    private const int AllDaysMask = 0x7F;

    public static readonly ShopCalendar MondayToFriday =
        new(WorkCenterCapacity.MondayToFridayMask, new HashSet<DateOnly>());

    public static async Task<ShopCalendar> LoadDefaultAsync(AppDbContext db, CancellationToken ct)
    {
        var calendar = await db.WorkingCalendars
            .AsNoTracking()
            .Include(c => c.Holidays)
            .Where(c => c.IsDefault && c.IsActive)
            .FirstOrDefaultAsync(ct);

        return calendar is null
            ? MondayToFriday
            : new ShopCalendar(
                calendar.WorkingDaysMask,
                calendar.Holidays.Select(h => h.ObservedDate ?? h.Date).ToHashSet());
    }

    public static DateOnly DateOf(DateTimeOffset moment) => DateOnly.FromDateTime(moment.UtcDateTime);

    public bool IsWorkingDay(DateOnly date) =>
        (EffectiveMask & (1 << (int)date.DayOfWeek)) != 0 && !Holidays.Contains(date);

    public int CalendarDaysAfter(DateOnly start, int workingDays) => Span(start, workingDays, 1);

    public int CalendarDaysBefore(DateOnly end, int workingDays) => Span(end, workingDays, -1);

    private int EffectiveMask => (WorkingDaysMask & AllDaysMask) == 0 ? AllDaysMask : WorkingDaysMask;

    private int Span(DateOnly anchor, int workingDays, int step)
    {
        var remaining = Math.Max(workingDays, 0);
        var current = anchor;
        var days = 0;
        while (remaining > 0 && days < workingDays * 7 + 366)
        {
            current = current.AddDays(step);
            days++;
            if (IsWorkingDay(current))
                remaining--;
        }

        return days;
    }
}
