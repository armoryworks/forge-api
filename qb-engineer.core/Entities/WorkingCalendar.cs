namespace QBEngineer.Core.Entities;

/// <summary>
/// Defines what counts as a working day vs a holiday for business-day
/// calculations. Owned by a tenant (default calendar) and may be overridden
/// per CompanyLocation. Drives every business-days helper in the system —
/// vendor lead time, MRP, maintenance schedules, SLA timers, etc.
///
/// <para>WorkingDaysMask is a 7-bit mask: bit 0 = Sunday, bit 1 = Monday,
/// ..., bit 6 = Saturday. Most US installs use 0b0111110 (62) for Mon–Fri.</para>
///
/// <para>Holidays are a child collection. Recurring holidays (Independence
/// Day on July 4 every year) generate concrete Holiday rows lazily — see
/// HolidayMaterializerJob (TODO Phase 2).</para>
/// </summary>
public class WorkingCalendar : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IANA timezone identifier (e.g., "America/Denver"). Used to resolve
    /// "today" when the user's wall-clock crosses a UTC day boundary mid-shift.
    /// </summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>
    /// Bitmask of working days. Bit 0 = Sun, bit 1 = Mon, ..., bit 6 = Sat.
    /// Default 0b0111110 (62) = Mon–Fri.
    /// </summary>
    public int WorkingDaysMask { get; set; } = 62;

    /// <summary>
    /// True when this calendar is the system-wide default. Exactly one
    /// WorkingCalendar per install has IsDefault = true (enforced by a
    /// filtered unique index).
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Soft-disable a calendar without deleting it. Inactive calendars can't
    /// be assigned to new locations but existing references are preserved.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Holidays observed by this calendar. Includes both fixed-date and
    /// observed-date entries (e.g., Independence Day = July 4 fixed; if
    /// it falls on a Saturday the observed date is the prior Friday).
    /// </summary>
    public ICollection<Holiday> Holidays { get; set; } = new List<Holiday>();
}
