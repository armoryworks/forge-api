namespace Forge.Core.Entities.Accounting;

/// <summary>
/// Per-(book, fiscal-year) counter for allocating <c>JournalEntry.EntryNumber</c>.
/// Allocated via a row-locked <c>UPDATE … RETURNING</c> (the safe
/// <c>JobRepository</c> pattern, NOT <c>InvoiceRepository</c>'s read-max+1).
/// Gaps are allowed and documented (§5.1).
/// </summary>
public class AcctNumberSequence : BaseEntity
{
    public int BookId { get; set; }

    public int FiscalYearId { get; set; }

    /// <summary>Next <c>EntryNumber</c> to hand out for this (book, year).</summary>
    public long NextValue { get; set; } = 1;

    public Book Book { get; set; } = null!;
    public FiscalYear FiscalYear { get; set; } = null!;
}
