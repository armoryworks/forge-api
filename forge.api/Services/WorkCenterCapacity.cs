using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Services;

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

    public static readonly WorkCenterCapacity None =
        new([], new Dictionary<DateOnly, decimal>(), 0m, 0, new HashSet<DateOnly>(), 1m, 1);

    public decimal HoursOn(DateOnly date)
    {
        if (Overrides.TryGetValue(date, out var overrideHours))
            return overrideHours;

        if (Shifts.Count > 0)
            return ShiftHoursOn(date);

        return IsWorkingDay(date) ? DailyCapacityHours : 0m;
    }

    public decimal MachineHoursOn(DateOnly date) => HoursOn(date) * Machines;

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
