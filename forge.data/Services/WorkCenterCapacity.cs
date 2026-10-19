using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Data.Services;

public sealed record WorkCenterCapacity(
    IReadOnlyList<WorkCenterShiftInfo> Shifts,
    IReadOnlyDictionary<DateOnly, decimal> Overrides,
    decimal DailyCapacityHours,
    int WorkingDaysMask,
    IReadOnlySet<DateOnly> Holidays,
    decimal Efficiency,
    int Machines)
{
    public const int MondayToFridayMask = 62;

    public static readonly TimeOnly DefaultDayStart = new(8, 0);

    public static readonly WorkCenterCapacity None =
        new([], new Dictionary<DateOnly, decimal>(), 0m, 0, new HashSet<DateOnly>(), 1m, 1);

    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    public TimeOnly DayStart { get; init; } = DefaultDayStart;

    public decimal HoursOn(DateOnly date)
    {
        if (Overrides.TryGetValue(date, out var overrideHours))
            return overrideHours;

        if (Shifts.Count > 0)
            return Holidays.Contains(date) ? 0m : ShiftHoursOn(date);

        return IsWorkingDay(date) ? DailyCapacityHours : 0m;
    }

    public decimal MachineHoursOn(DateOnly date) => HoursOn(date) * Machines;

    public DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);

    public DateTimeOffset LocalTimeUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZone.GetUtcOffset(local)).ToUniversalTime();
    }

    public DateTimeOffset DayStartUtc(DateOnly date) => LocalTimeUtc(date, DayStart);

    public DateTimeOffset MidnightUtc(DateOnly date) => LocalTimeUtc(date, TimeOnly.MinValue);

    private decimal ShiftHoursOn(DateOnly date)
    {
        var dayFlag = date.DayOfWeek switch
        {
            DayOfWeek.Monday => DaysOfWeek.Monday,
            DayOfWeek.Tuesday => DaysOfWeek.Tuesday,
            DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
            DayOfWeek.Thursday => DaysOfWeek.Thursday,
            DayOfWeek.Friday => DaysOfWeek.Friday,
            DayOfWeek.Saturday => DaysOfWeek.Saturday,
            DayOfWeek.Sunday => DaysOfWeek.Sunday,
            _ => DaysOfWeek.None,
        };

        return Shifts.Where(s => s.DaysOfWeek.HasFlag(dayFlag)).Sum(s => s.NetHours);
    }

    private bool IsWorkingDay(DateOnly date) =>
        (WorkingDaysMask & (1 << (int)date.DayOfWeek)) != 0 && !Holidays.Contains(date);
}
