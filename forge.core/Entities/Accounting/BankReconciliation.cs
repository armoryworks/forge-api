using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// ⚡ Phase-3 — a bank reconciliation of one cash GL account against a bank statement. Clearing state lives
/// here (in <see cref="BankReconciliationItem"/>), NOT on the immutable journal lines: an item flags whether
/// a cash line has cleared the bank. Reconciled when the statement ending balance plus the net of still-
/// outstanding items equals the GL cash balance.
/// </summary>
public class BankReconciliation : BaseAuditableEntity, IConcurrencyVersioned
{
    public int BookId { get; set; }

    /// <summary>The cash GL account being reconciled.</summary>
    public int CashGlAccountId { get; set; }

    public DateOnly StatementDate { get; set; }

    /// <summary>The bank statement's ending balance (absolute).</summary>
    public decimal StatementEndingBalance { get; set; }

    public BankReconciliationStatus Status { get; set; } = BankReconciliationStatus.Draft;

    public DateTimeOffset? FinalizedAt { get; set; }

    public uint Version { get; set; }

    public ICollection<BankReconciliationItem> Items { get; set; } = [];
}

/// <summary>One cash journal line considered by a reconciliation, with its cleared flag.</summary>
public class BankReconciliationItem : BaseEntity
{
    public int BankReconciliationId { get; set; }

    /// <summary>The cash-account <see cref="JournalLine"/> (immutable; this row carries the mutable cleared flag).</summary>
    public long JournalLineId { get; set; }

    public bool IsCleared { get; set; }

    public BankReconciliation BankReconciliation { get; set; } = null!;
    public JournalLine JournalLine { get; set; } = null!;
}
