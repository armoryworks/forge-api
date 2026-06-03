using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// A single debit/credit line of a <see cref="JournalEntry"/>. DB CHECK
/// <c>(Debit = 0) &lt;&gt; (Credit = 0)</c> enforces exactly one non-zero side
/// (rejects 0/0 and both-non-zero); the engine re-validates. Uses a
/// <see cref="long"/> Id (so it does NOT derive from <c>BaseEntity</c>), which
/// also keeps it out of the global soft-delete query filter (§5.1).
/// <para>
/// Party fields stay polymorphic (a control line's counterparty is a Customer
/// <b>or</b> a Vendor → cannot FK-enforce); the engine requires them on control
/// lines.
/// </para>
/// </summary>
public class JournalLine
{
    public long Id { get; set; }

    public long JournalEntryId { get; set; }

    /// <summary>Denormalized from the entry for book-consistency checks + reads.</summary>
    public int BookId { get; set; }

    public int LineNumber { get; set; }

    public int GlAccountId { get; set; }

    // Dimensions
    public int? JobId { get; set; }
    public int? CostCenterId { get; set; }

    /// <summary>&gt;= 0; exactly one of Debit/Credit is non-zero (DB CHECK).</summary>
    public decimal Debit { get; set; }

    /// <summary>&gt;= 0; exactly one of Debit/Credit is non-zero (DB CHECK).</summary>
    public decimal Credit { get; set; }

    public int CurrencyId { get; set; }

    /// <summary>Transaction-currency amount. Phase-0 single-currency: equals FunctionalAmount.</summary>
    public decimal TxnAmount { get; set; }

    /// <summary>Functional-currency amount. Phase-0: FxRate = 1, equals TxnAmount.</summary>
    public decimal FunctionalAmount { get; set; }

    /// <summary>FX rate txn→functional. Pinned to 1 in Phase 0 (§5.2).</summary>
    public decimal FxRate { get; set; }

    // Polymorphic sub-ledger counterparty (required on control lines by the engine)
    public SubledgerPartyType? SubledgerPartyType { get; set; }
    public int? SubledgerPartyId { get; set; }

    public string? Description { get; set; }

    public JournalEntry JournalEntry { get; set; } = null!;
    public GlAccount GlAccount { get; set; } = null!;
    public Job? Job { get; set; }
    public CostCenter? CostCenter { get; set; }
    public Currency Currency { get; set; } = null!;
}
