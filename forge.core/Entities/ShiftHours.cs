namespace Forge.Core.Entities;

/// <summary>
/// Hour arithmetic shared by every place that reads a <see cref="Shift"/>:
/// wall-clock span (wrapping past midnight), net hours after breaks, and
/// the effective capacity fallback chain CapacityHours → NetHours → span.
/// </summary>
public static class ShiftHours
{
    /// <summary>Wall-clock length of the window; an end at or before the start wraps past midnight.</summary>
    public static decimal Span(TimeOnly start, TimeOnly end)
    {
        var span = end > start
            ? end.ToTimeSpan() - start.ToTimeSpan()
            : TimeSpan.FromHours(24) - start.ToTimeSpan() + end.ToTimeSpan();
        return (decimal)span.TotalHours;
    }

    /// <summary>Span less the unpaid break.</summary>
    public static decimal Net(TimeOnly start, TimeOnly end, int breakMinutes) =>
        Span(start, end) - (breakMinutes / 60m);

    /// <summary>Capacity hours when set, else net hours, else the wall-clock span.</summary>
    public static decimal EffectiveCapacity(decimal capacityHours, decimal netHours, TimeOnly start, TimeOnly end)
    {
        if (capacityHours > 0) return capacityHours;
        if (netHours > 0) return netHours;
        return Span(start, end);
    }

    /// <summary><see cref="EffectiveCapacity(decimal, decimal, TimeOnly, TimeOnly)"/> for a shift row.</summary>
    public static decimal EffectiveCapacity(Shift shift) =>
        EffectiveCapacity(shift.CapacityHours, shift.NetHours, shift.StartTime, shift.EndTime);
}
