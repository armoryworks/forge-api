namespace Forge.Core.Entities;

/// <summary>
/// A recurring work window. Two consumption modes share the same row:
///
/// <list type="bullet">
///   <item><b>Work-center template</b> (legacy / scheduling): rows with
///   <see cref="WorkingCalendarId"/> = null are reusable templates
///   assigned to work centers via <see cref="WorkCenterShift"/>. Days of
///   week live on the junction. Used by the scheduling feature.</item>
///   <item><b>Calendar-scoped shift</b> (Shifts effort): rows with
///   <see cref="WorkingCalendarId"/> set bind directly to a
///   <see cref="WorkingCalendar"/> and own their own
///   <see cref="DaysOfWeekMask"/>. Used by hours-of-operation, SLA
///   timers, and (future) payroll premium.</item>
/// </list>
///
/// <para>The split lets the existing scheduling work keep its
/// junction-day model while the bought-parts effort layers calendar-bound
/// shifts on top without breaking either.</para>
/// </summary>
public class Shift : BaseAuditableEntity
{
    public string Name { get; set; } = "";
    public TimeOnly StartTime { get; set; }

    /// <summary>
    /// Shift end time-of-day. When &lt;= <see cref="StartTime"/>, the
    /// shift is treated as spanning midnight (graveyard shift).
    /// </summary>
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public decimal NetHours { get; set; }
    public bool IsActive { get; set; } = true;

    // ─── Shifts effort ──────────────────────────────────────────────────
    /// <summary>
    /// Calendar this shift is bound to. Null = legacy work-center
    /// template (days live on <see cref="WorkCenterShift"/>); set =
    /// calendar-scoped shift, drives hours-of-operation + payroll
    /// premium.
    /// </summary>
    public int? WorkingCalendarId { get; set; }

    /// <summary>
    /// 7-bit mask: bit 0 = Sun, bit 1 = Mon, ..., bit 6 = Sat. Used
    /// when <see cref="WorkingCalendarId"/> is set. Null on legacy rows.
    /// Defaults to 0b0111110 (62) = Mon–Fri on calendar-scoped rows.
    /// </summary>
    public int? DaysOfWeekMask { get; set; }

    /// <summary>
    /// Pay-rate multiplier when a clock event falls within this shift.
    /// Standard = 1.00, time-and-a-half = 1.50, holiday/double = 2.00.
    /// Persisted now so payroll has the contract ready; payroll wiring
    /// is a follow-up effort.
    ///
    /// <para><b>TODO (Shifts effort, Slice 3 — payroll premium application):</b>
    /// the multiplier is captured here but not yet consumed by anything.
    /// When payroll picks this up, the integration points are:</para>
    /// <list type="number">
    ///   <item>For each <c>ClockEvent</c> on a worker, resolve their
    ///   work location → <c>CompanyLocation.WorkingCalendarId</c> →
    ///   active <c>Shift</c> rows. Pick the shift whose window contains
    ///   the event's timestamp (use <c>IShiftService.IsWithinShiftAsync</c>;
    ///   it already handles graveyard wraparound).</item>
    ///   <item>Bucket the event's elapsed minutes by the shift's
    ///   <c>PremiumMultiplier</c>. Worker hours that fall outside any
    ///   active shift default to 1.00 (standard rate).</item>
    ///   <item>Apply the multiplier inside the pay-stub gross-wages
    ///   calc — likely in <c>PayStub</c>'s line aggregation. The pay
    ///   period rolls up "regular hours" + "OT hours" + "premium hours
    ///   at 1.5x" + "premium hours at 2x" so payroll exports stay
    ///   itemizable.</item>
    ///   <item>Edge cases worth deciding before implementing: do
    ///   overlapping shifts apply the higher multiplier or the more
    ///   recent one? Recommend higher (worker-favorable, simpler audit).
    ///   Same call when a clock event spans a shift boundary — split
    ///   the event into per-shift slices and apply each window's
    ///   multiplier.</item>
    /// </list>
    /// </summary>
    public decimal PremiumMultiplier { get; set; } = 1.00m;

    /// <summary>
    /// Effective labor capacity in hours per shift instance. Defaults to
    /// 0 — when 0 the helper services fall back to <see cref="NetHours"/>
    /// (or wall-clock duration when NetHours is also 0). Overridable so
    /// admins can carve out lunches / changeovers without modeling them
    /// as separate shifts. Feeds finite-capacity MRP.
    /// </summary>
    public decimal CapacityHours { get; set; }

    public WorkingCalendar? WorkingCalendar { get; set; }
    public ICollection<WorkCenterShift> WorkCenterShifts { get; set; } = [];
}
