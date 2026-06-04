namespace Forge.Core.Models.Accounting;

/// <summary>
/// Phase-1 STAGE E — Profit &amp; Loss (income statement) for a book over a period
/// range (ACCOUNTING_SUITE_PLAN §6 Phase-1 row "P&amp;L + Balance Sheet", §5.3).
/// Built over the same filter-immune ledger read the
/// <see cref="TrialBalance"/> uses, restricted to <c>Income</c> and
/// <c>Expense</c> accounts (<c>GlAccount.AccountType</c>) whose entries fall in
/// the <c>[FromDate, ToDate]</c> window. Amounts are <b>functional</b> currency
/// (Phase-0/1 single-currency invariant — TxnAmount == FunctionalAmount).
///
/// <para><b>Incomplete-margin caveat (Phase 1).</b> COGS is <b>not yet posted</b>
/// — inventory/COGS posting lands in Phase 2 (§6 Phase-2 row, §7 matrix Phase-2
/// rows). So although a Cost-of-Goods-Sold account exists in the seeded chart,
/// the income statement here reflects revenue and operating expense only; gross
/// margin is therefore <b>incomplete</b>. <see cref="CogsPosted"/> is
/// <c>false</c> and <see cref="MarginCaveat"/> spells this out so the report is
/// never mistaken for a complete margin statement. This ties to
/// <c>CAP-RPT-FINANCIALS</c> (default OFF until COGS posting is live — §6
/// sequencing note, §10).</para>
/// </summary>
public sealed class ProfitAndLoss
{
    public int BookId { get; init; }

    /// <summary>Inclusive start of the reporting window (null = from inception).</summary>
    public DateOnly? FromDate { get; init; }

    /// <summary>Inclusive end of the reporting window (null = open-ended).</summary>
    public DateOnly? ToDate { get; init; }

    /// <summary>Income (revenue) account lines, credit-normal, signed positive for revenue.</summary>
    public IReadOnlyList<ProfitAndLossLine> Income { get; init; } = [];

    /// <summary>Expense account lines, debit-normal, signed positive for expense.</summary>
    public IReadOnlyList<ProfitAndLossLine> Expense { get; init; } = [];

    /// <summary>Σ income (functional). Credit-normal: Cr − Dr across income accounts.</summary>
    public decimal TotalIncome { get; init; }

    /// <summary>Σ expense (functional). Debit-normal: Dr − Cr across expense accounts.</summary>
    public decimal TotalExpense { get; init; }

    /// <summary>Net income = <see cref="TotalIncome"/> − <see cref="TotalExpense"/>.</summary>
    public decimal NetIncome => TotalIncome - TotalExpense;

    /// <summary>
    /// <c>false</c> in Phase 1 — COGS is not posted yet (Phase 2). Surfaced so the
    /// consumer can label gross margin as incomplete.
    /// </summary>
    public bool CogsPosted { get; init; }

    /// <summary>
    /// Human-readable caveat repeated on the report so the incomplete-margin
    /// limitation travels with the data (not just the API docs).
    /// </summary>
    public string MarginCaveat { get; init; } = string.Empty;
}

/// <summary>
/// One Income or Expense account's net contribution to the P&amp;L over the window.
/// The <see cref="Amount"/> is signed in the account's natural statement
/// direction: revenue is positive for an Income account, expense is positive for
/// an Expense account (a contra account — e.g. Sales Returns, an Income account
/// with a debit normal balance — naturally nets negative against revenue).
/// </summary>
public sealed class ProfitAndLossLine
{
    public int GlAccountId { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;

    /// <summary>
    /// Net amount (functional) in the account's statement direction:
    /// Income → Cr − Dr; Expense → Dr − Cr.
    /// </summary>
    public decimal Amount { get; init; }
}
