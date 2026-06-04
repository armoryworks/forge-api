namespace Forge.Core.Models.Accounting;

/// <summary>
/// Phase-1 STAGE E — Balance Sheet (statement of financial position) for a book
/// <b>as of</b> a date (ACCOUNTING_SUITE_PLAN §6 Phase-1 row "P&amp;L + Balance
/// Sheet", §5.3). Built over the same filter-immune ledger read the
/// <see cref="TrialBalance"/> uses, restricted to <c>Asset</c>, <c>Liability</c>
/// and <c>Equity</c> accounts (<c>GlAccount.AccountType</c>) whose entries are
/// dated on/before the as-of date. Amounts are <b>functional</b> currency
/// (Phase-0/1 single-currency invariant — TxnAmount == FunctionalAmount).
///
/// <para><b>Current-year-earnings.</b> Income/Expense activity is not rolled into
/// Retained Earnings until year-end close (Phase 3, §6 / §12). To make the sheet
/// balance before that close, the net income earned <b>within the current fiscal
/// year</b> up to the as-of date is surfaced as a computed equity line
/// (<see cref="CurrentYearEarnings"/>) — the standard interim balance-sheet
/// treatment. <see cref="TotalEquityWithEarnings"/> folds it in so
/// Assets = Liabilities + Equity (incl. current-year earnings).</para>
///
/// <para><b>Incomplete-margin caveat (Phase 1).</b> Because COGS is not posted yet
/// (Phase 2), the current-year-earnings figure inherits the incomplete-margin
/// limitation of the P&amp;L (<see cref="CogsPosted"/> = <c>false</c>,
/// <see cref="MarginCaveat"/>). Inventory still carries its conversion/opening
/// control balance; it is simply not relieved to COGS at the sale until Phase 2.</para>
/// </summary>
public sealed class BalanceSheet
{
    public int BookId { get; init; }

    /// <summary>The date the position is struck (inclusive).</summary>
    public DateOnly AsOfDate { get; init; }

    /// <summary>Asset account lines (debit-normal), signed positive for a normal asset.</summary>
    public IReadOnlyList<BalanceSheetLine> Assets { get; init; } = [];

    /// <summary>Liability account lines (credit-normal), signed positive for a normal liability.</summary>
    public IReadOnlyList<BalanceSheetLine> Liabilities { get; init; } = [];

    /// <summary>
    /// Equity account lines (credit-normal), signed positive for normal equity.
    /// Does <b>not</b> include the computed <see cref="CurrentYearEarnings"/> line
    /// (which is derived from Income/Expense, not posted to an equity account
    /// until year-end close).
    /// </summary>
    public IReadOnlyList<BalanceSheetLine> Equity { get; init; } = [];

    /// <summary>Σ assets (functional, debit-normal: Dr − Cr).</summary>
    public decimal TotalAssets { get; init; }

    /// <summary>Σ liabilities (functional, credit-normal: Cr − Dr).</summary>
    public decimal TotalLiabilities { get; init; }

    /// <summary>Σ posted equity accounts (functional, credit-normal: Cr − Dr), excluding current-year earnings.</summary>
    public decimal TotalEquityPosted { get; init; }

    /// <summary>
    /// Net income earned within the current fiscal year through the as-of date
    /// (Income − Expense over [fiscal-year-start, as-of]). Surfaced as an equity
    /// line so the sheet balances before the Phase-3 year-end RE roll. Zero when
    /// no current fiscal year covers the as-of date.
    /// </summary>
    public decimal CurrentYearEarnings { get; init; }

    /// <summary>Total equity including the computed current-year-earnings line.</summary>
    public decimal TotalEquityWithEarnings => TotalEquityPosted + CurrentYearEarnings;

    /// <summary>Liabilities + equity (incl. current-year earnings) — the credit side.</summary>
    public decimal TotalLiabilitiesAndEquity => TotalLiabilities + TotalEquityWithEarnings;

    /// <summary>
    /// True when Assets == Liabilities + Equity (incl. current-year earnings). A
    /// posted ledger always balances, so this holds by construction; a non-zero
    /// difference signals an out-of-band mutation (a bug to alert on).
    /// </summary>
    public bool IsBalanced => TotalAssets == TotalLiabilitiesAndEquity;

    /// <summary>
    /// <c>false</c> in Phase 1 — COGS is not posted yet (Phase 2), so the
    /// current-year-earnings figure (and therefore equity) reflects an incomplete
    /// gross margin.
    /// </summary>
    public bool CogsPosted { get; init; }

    /// <summary>Human-readable caveat repeated on the report (see <see cref="CogsPosted"/>).</summary>
    public string MarginCaveat { get; init; } = string.Empty;
}

/// <summary>
/// One Asset / Liability / Equity account's net balance on the balance sheet as of
/// the report date. The <see cref="Amount"/> is signed in the account's natural
/// statement direction: an Asset (debit-normal) is positive when Dr &gt; Cr; a
/// Liability/Equity (credit-normal) is positive when Cr &gt; Dr.
/// </summary>
public sealed class BalanceSheetLine
{
    public int GlAccountId { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;

    /// <summary>
    /// Net balance (functional) in the account's statement direction:
    /// Asset → Dr − Cr; Liability/Equity → Cr − Dr.
    /// </summary>
    public decimal Amount { get; init; }
}
