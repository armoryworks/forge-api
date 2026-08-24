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

    // ── Comparative period (optional) ────────────────────────────────────────
    // Populated only when the caller requests a comparison. All-null on the
    // default (single-period) statement, so existing callers are unaffected. The
    // per-line prior/variance figures live on ProfitAndLossLine; these are the
    // matching statement-total figures. Variance getters delegate to
    // StatementVariance so lines and totals share one divide-by-zero-guarded rule.

    /// <summary>Inclusive start of the comparison window, when a comparison is in effect.</summary>
    public DateOnly? CompareFromDate { get; init; }

    /// <summary>Inclusive end of the comparison window, when a comparison is in effect.</summary>
    public DateOnly? CompareToDate { get; init; }

    /// <summary><c>true</c> when this statement carries a prior-period comparison.</summary>
    public bool HasComparison => CompareFromDate is not null || CompareToDate is not null;

    /// <summary>Σ income for the comparison window; <c>null</c> when no comparison.</summary>
    public decimal? PriorTotalIncome { get; init; }

    /// <summary>Σ expense for the comparison window; <c>null</c> when no comparison.</summary>
    public decimal? PriorTotalExpense { get; init; }

    /// <summary>Prior-period net income; <c>null</c> when no comparison.</summary>
    public decimal? PriorNetIncome { get; init; }

    public decimal? TotalIncomeVariance => StatementVariance.Variance(TotalIncome, PriorTotalIncome);
    public decimal? TotalIncomeVariancePercent => StatementVariance.Percent(TotalIncome, PriorTotalIncome);
    public decimal? TotalExpenseVariance => StatementVariance.Variance(TotalExpense, PriorTotalExpense);
    public decimal? TotalExpenseVariancePercent => StatementVariance.Percent(TotalExpense, PriorTotalExpense);
    public decimal? NetIncomeVariance => StatementVariance.Variance(NetIncome, PriorNetIncome);
    public decimal? NetIncomeVariancePercent => StatementVariance.Percent(NetIncome, PriorNetIncome);

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

    /// <summary>
    /// The same account's amount in the comparison window. <c>null</c> when no
    /// comparison is in effect; <c>0</c> when the account had no activity in the
    /// prior window but did this window (so variance reads as the full movement).
    /// </summary>
    public decimal? PriorAmount { get; init; }

    /// <summary>Current − prior; <c>null</c> when no comparison.</summary>
    public decimal? Variance => StatementVariance.Variance(Amount, PriorAmount);

    /// <summary>Signed % movement vs prior; <c>null</c> when no comparison or prior is zero.</summary>
    public decimal? VariancePercent => StatementVariance.Percent(Amount, PriorAmount);
}
