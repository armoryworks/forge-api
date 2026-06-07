using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ Phase-3 — a bank reconciliation worksheet. The GL cash balance, the bank statement ending balance, the
/// cash lines (cleared vs outstanding), and the reconciling difference. Reconciled when the statement ending
/// balance plus the net of still-outstanding items equals the GL cash balance.
/// </summary>
public sealed class BankReconciliationWorksheet
{
    public int ReconciliationId { get; init; }
    public int BookId { get; init; }
    public int CashGlAccountId { get; init; }
    public DateOnly StatementDate { get; init; }
    public decimal StatementEndingBalance { get; init; }
    public BankReconciliationStatus Status { get; init; }

    /// <summary>GL cash balance as of the statement date (Σ cash-line net debit).</summary>
    public decimal BookBalance { get; init; }

    public IReadOnlyList<BankReconciliationItemRow> Items { get; init; } = [];

    public decimal ClearedTotal { get; init; }
    public decimal OutstandingTotal { get; init; }

    /// <summary>BookBalance − OutstandingTotal − StatementEndingBalance. Zero when reconciled.</summary>
    public decimal Difference => BookBalance - OutstandingTotal - StatementEndingBalance;

    public decimal RoundingTolerance { get; init; }
    public bool IsReconciled => Math.Abs(Difference) <= RoundingTolerance;
}

/// <summary>A cash GL account available to reconcile (the CASH determination key).</summary>
public sealed record CashAccountModel(int GlAccountId, string AccountNumber, string Name);

/// <summary>A reconciliation summary row (the list view).</summary>
public sealed record BankReconciliationSummary(
    int ReconciliationId,
    int CashGlAccountId,
    string CashAccountName,
    DateOnly StatementDate,
    decimal StatementEndingBalance,
    BankReconciliationStatus Status,
    decimal Difference,
    bool IsReconciled);

/// <summary>One cash line on the worksheet (amount = its cash effect: + deposit, − withdrawal).</summary>
public sealed class BankReconciliationItemRow
{
    public int ItemId { get; init; }
    public long JournalLineId { get; init; }
    public long JournalEntryId { get; init; }
    public DateOnly EntryDate { get; init; }
    public string? Description { get; init; }
    public decimal Amount { get; init; }
    public bool IsCleared { get; init; }
}
