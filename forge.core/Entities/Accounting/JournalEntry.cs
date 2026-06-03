using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// Journal-entry header. <b>Append-only once <see cref="JournalEntryStatus.Posted"/></b>
/// — the ONLY mutation permitted on a Posted row is the single
/// <see cref="JournalEntryStatus.Posted"/>→<see cref="JournalEntryStatus.Reversed"/>
/// status flip plus the <see cref="ReversedByEntryId"/> link, which the
/// immutability interceptor explicitly allows (§5.1, §5.2).
/// <para>
/// Uses a <see cref="long"/> Id (cheap pre-rows; painful later) and therefore
/// does NOT derive from <c>BaseEntity</c>/<c>BaseAuditableEntity</c>; this also
/// keeps it out of the global soft-delete query filter. Per §4 it is
/// deliberately NOT <c>IConcurrencyVersioned</c> (immutability is enforced by
/// the interceptor + Postgres trigger, not optimistic locking).
/// </para>
/// </summary>
public class JournalEntry
{
    public long Id { get; set; }

    public int BookId { get; set; }

    /// <summary>Monotonic per book/year — gaps allowed (US-GAAP imposes no gapless mandate).</summary>
    public long EntryNumber { get; set; }

    /// <summary>
    /// Resolves the period in the book's ReportingTimeZone; immune to UTC
    /// normalization (<see cref="DateOnly"/>).
    /// </summary>
    public DateOnly EntryDate { get; set; }

    public int FiscalPeriodId { get; set; }

    /// <summary>
    /// Fiscal year of <see cref="FiscalPeriodId"/>, denormalized so EntryNumber
    /// can be made UNIQUE per (BookId, FiscalYearId) — EntryNumber is monotonic
    /// per book/year (§5.1).
    /// </summary>
    public int FiscalYearId { get; set; }

    public JournalSource Source { get; set; }

    /// <summary>Polymorphic source link — type half.</summary>
    public string? SourceType { get; set; }

    /// <summary>Polymorphic source link — id half.</summary>
    public long? SourceId { get; set; }

    /// <summary>
    /// Non-null for ALL non-Manual sources (incl. Conversion + recurring).
    /// Key shape <c>source:type:id:purpose</c>; UNIQUE per (BookId, key) — a
    /// duplicate returns the existing entry (no throw) (§5.1, §5.2).
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public int CurrencyId { get; set; }

    public string? Memo { get; set; }

    public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Draft;

    /// <summary>
    /// Accrual flag — the period-close step reverses these into the next period.
    /// </summary>
    public bool AutoReverseNextPeriod { get; set; }

    /// <summary>Set on a reversal entry, pointing at the entry it reverses.</summary>
    public long? ReversalOfEntryId { get; set; }

    /// <summary>Set on the original entry once it has been reversed.</summary>
    public long? ReversedByEntryId { get; set; }

    public int? ApprovedBy { get; set; }
    public int? PostedBy { get; set; }
    public DateTimeOffset? PostedAt { get; set; }

    public Book Book { get; set; } = null!;
    public FiscalPeriod FiscalPeriod { get; set; } = null!;
    public FiscalYear FiscalYear { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public JournalEntry? ReversalOfEntry { get; set; }
    public JournalEntry? ReversedByEntry { get; set; }
    public ICollection<JournalLine> Lines { get; set; } = [];
}
