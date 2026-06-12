namespace Forge.Core.Entities.Accounting;

/// <summary>
/// QB-001 — one row per journal-summary push to QuickBooks Online: the period
/// range pushed, the QBO JournalEntry doc id that came back, the total debit
/// (== total credit — the push is balanced by construction), and who pushed
/// when. Re-pushing a range that overlaps an existing log row requires
/// <c>force=true</c> (idempotent-by-warning).
/// </summary>
public class QboExportLog : BaseAuditableEntity
{
    public int BookId { get; set; }

    /// <summary>Inclusive start of the pushed period.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Inclusive end of the pushed period.</summary>
    public DateOnly ToDate { get; set; }

    /// <summary>The QuickBooks Online JournalEntry id returned by the push.</summary>
    public string QboDocId { get; set; } = string.Empty;

    /// <summary>Total debit of the pushed JE (equals total credit).</summary>
    public decimal TotalDebit { get; set; }

    public DateTimeOffset PushedAt { get; set; }

    /// <summary>User id of the controller who pushed (null = system).</summary>
    public int? PushedBy { get; set; }
}
