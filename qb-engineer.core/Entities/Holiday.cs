namespace QBEngineer.Core.Entities;

/// <summary>
/// A single holiday observed by a WorkingCalendar. The Date is the
/// EFFECTIVE non-working day (use ObservedDate to capture when a fixed
/// holiday slides because it falls on a weekend — e.g., July 4 falls
/// on a Saturday → Date = Saturday, ObservedDate = Friday). Business-day
/// calculations exclude rows where ObservedDate ?? Date matches the
/// calendar day being checked.
///
/// <para>IsRecurring=true means "this holiday repeats every year on the
/// same MM-DD." A nightly Hangfire job (HolidayMaterializerJob, TODO
/// Phase 2) generates the next year's concrete rows ahead of time so
/// runtime queries are simple equality lookups.</para>
/// </summary>
public class Holiday : BaseEntity
{
    public int WorkingCalendarId { get; set; }
    public WorkingCalendar? WorkingCalendar { get; set; }

    /// <summary>
    /// The actual non-working day. For non-recurring or first-instance
    /// rows this is the literal calendar date.
    /// </summary>
    public DateOnly Date { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// When set, business-day calculations use this date instead of Date
    /// — covers the "Independence Day falls on Saturday → observed
    /// Friday" case. When null, Date is the observed date.
    /// </summary>
    public DateOnly? ObservedDate { get; set; }

    /// <summary>
    /// True for recurring holidays. The materializer extends the row set
    /// year-over-year so runtime lookups stay simple.
    /// </summary>
    public bool IsRecurring { get; set; }
}
