using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// Phase-1 STAGE E read seam (ACCOUNTING_SUITE_PLAN §6 Phase-1 row "P&amp;L +
/// Balance Sheet", §5.3). Produces the two basic financial statements
/// <b>from the ledger</b>, over the same filter-immune
/// <c>JournalLine</c>/<c>GlAccount</c> projection the
/// <see cref="ITrialBalanceService"/> uses, classified by
/// <c>GlAccount.AccountType</c>:
/// <list type="bullet">
///   <item><b>Profit &amp; Loss</b> — Income/Expense accounts over a period range.</item>
///   <item><b>Balance Sheet</b> — Asset/Liability/Equity accounts as of a date,
///   plus a computed current-year-earnings equity line so the sheet balances
///   before the Phase-3 year-end Retained-Earnings roll.</item>
/// </list>
/// <para>
/// <b>Filter-immune</b>, like the trial balance (§5.3): every read uses
/// <c>IgnoreQueryFilters</c> so a soft-deleted ledger row never silently drops
/// and makes a statement appear to balance when it does not.
/// </para>
/// <para>
/// <b>Phase-1 margin caveat.</b> COGS is not posted yet (Phase 2), so gross
/// margin on the P&amp;L — and therefore current-year-earnings on the balance
/// sheet — is incomplete; both statements carry a <c>CogsPosted = false</c> flag
/// and a margin caveat. This ties to <c>CAP-RPT-FINANCIALS</c> (default OFF until
/// COGS posting is live).
/// </para>
/// </summary>
public interface IFinancialStatementService
{
    /// <summary>
    /// Builds the Profit &amp; Loss for <paramref name="bookId"/> over the optional
    /// <paramref name="fromDate"/>..<paramref name="toDate"/> window (null bounds =
    /// open-ended). Income and Expense lines are signed in their statement
    /// direction; net income = Σ income − Σ expense.
    /// </summary>
    Task<ProfitAndLoss> GetProfitAndLossAsync(
        int bookId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken ct = default);

    /// <summary>
    /// Builds the Balance Sheet for <paramref name="bookId"/> as of
    /// <paramref name="asOfDate"/> (null = today in the engine's clock). Asset,
    /// Liability and Equity lines are signed in their statement direction; a
    /// computed current-year-earnings equity line folds in net income earned
    /// within the current fiscal year up to the as-of date.
    /// </summary>
    Task<BalanceSheet> GetBalanceSheetAsync(
        int bookId,
        DateOnly? asOfDate = null,
        CancellationToken ct = default);
}
