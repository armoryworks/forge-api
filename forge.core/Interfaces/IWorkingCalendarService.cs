namespace Forge.Core.Interfaces;

/// <summary>
/// Helpers for business-day arithmetic. All methods take an optional
/// calendarId; when null, resolves to the tenant default. Callers that
/// know the relevant CompanyLocation should pass that location's
/// resolved calendar id (location.WorkingCalendarId ?? tenant default).
///
/// <para>"Calendar day" math (AddDays, etc.) doesn't need this service —
/// use DateOnly arithmetic directly. This interface is for the cases
/// where weekends + holidays must be skipped.</para>
/// </summary>
public interface IWorkingCalendarService
{
    /// <summary>
    /// Returns start + n business days, skipping weekends and holidays
    /// per the resolved calendar. n may be negative.
    /// </summary>
    Task<DateOnly> AddBusinessDaysAsync(DateOnly start, int n, int? calendarId = null, CancellationToken ct = default);

    /// <summary>
    /// Number of business days strictly between start and end (exclusive
    /// of both endpoints). Returns 0 if end &lt;= start.
    /// </summary>
    Task<int> BusinessDaysBetweenAsync(DateOnly start, DateOnly end, int? calendarId = null, CancellationToken ct = default);

    /// <summary>
    /// True when the given date is a working day on the resolved calendar
    /// (not a weekend per the calendar's WorkingDaysMask, and not a holiday).
    /// </summary>
    Task<bool> IsWorkingDayAsync(DateOnly date, int? calendarId = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the next working day on/after the given date. Returns date
    /// itself when it's already a working day.
    /// </summary>
    Task<DateOnly> NextWorkingDayAsync(DateOnly date, int? calendarId = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the resolved calendar id — the explicit calendarId if non-null,
    /// otherwise the tenant default. Throws if no default exists yet.
    /// </summary>
    Task<int> ResolveCalendarIdAsync(int? calendarId = null, CancellationToken ct = default);
}
