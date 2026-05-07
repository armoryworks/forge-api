namespace QBEngineer.Core.Interfaces;

/// <summary>
/// Helpers for calendar-bound <c>Shift</c> rows. Two consumer-facing
/// questions today:
///
/// <list type="bullet">
///   <item><b>Capacity per week</b> for finite-MRP / scheduling — sum of
///   shift CapacityHours across days the shift covers.</item>
///   <item><b>Are we open at moment X?</b> for SLA timers and pickup
///   UX — does any active shift on the calendar contain this moment?</item>
/// </list>
///
/// <para>Both ignore non-calendar-bound shifts (those without
/// <c>WorkingCalendarId</c>) since those exist only as work-center
/// templates and aren't part of the plant's hours-of-operation.</para>
///
/// <para>Payroll-premium application (clock event ∈ shift → multiplier
/// applied) is a future slice — schema is ready in
/// <c>Shift.PremiumMultiplier</c> but no service method here yet.</para>
/// </summary>
public interface IShiftService
{
    /// <summary>
    /// Sum of <c>CapacityHours × occurrences-per-week</c> across all
    /// active calendar-bound shifts on the given calendar. Returns 0
    /// when no shifts are configured. Uses each shift's
    /// <c>CapacityHours</c> when set, otherwise falls back to NetHours,
    /// otherwise wall-clock duration.
    /// </summary>
    Task<decimal> GetWeeklyCapacityHoursAsync(int workingCalendarId, CancellationToken ct);

    /// <summary>
    /// Is the given moment inside any active shift window on the
    /// calendar? Resolves the moment in the calendar's time zone, picks
    /// active shifts whose <c>DaysOfWeekMask</c> includes the resolved
    /// day-of-week, and tests <c>StartTime ≤ now &lt; EndTime</c>
    /// (or the wraparound equivalent for graveyard shifts). Returns
    /// false when no shifts are configured.
    /// </summary>
    Task<bool> IsWithinShiftAsync(int workingCalendarId, DateTimeOffset moment, CancellationToken ct);
}
