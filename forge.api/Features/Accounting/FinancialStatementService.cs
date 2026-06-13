using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-1 STAGE E — Profit &amp; Loss and Balance Sheet built over the ledger
/// (ACCOUNTING_SUITE_PLAN §6 Phase-1 row "P&amp;L + Balance Sheet", §5.3). Both
/// statements project the same filter-immune posted
/// <see cref="Forge.Core.Entities.Accounting.JournalLine"/> data the
/// <see cref="TrialBalanceService"/> reads, classified by
/// <c>GlAccount.AccountType</c>:
/// <list type="bullet">
///   <item>P&amp;L → Income/Expense accounts over a period range.</item>
///   <item>Balance Sheet → Asset/Liability/Equity accounts as of a date, with a
///   computed current-year-earnings equity line.</item>
/// </list>
///
/// <para><b>Filter-immune</b> (§5.3): every read uses <c>IgnoreQueryFilters</c> so
/// a soft-deleted ledger row never silently drops and makes a statement appear to
/// balance when it does not (ledger entities opt out of the global filter anyway;
/// the query asserts it).</para>
///
/// <para><b>Reversal handling</b> matches the trial balance: a Reversed original
/// is itself Posted-then-Reversed and its reversal is Posted+equal-and-opposite,
/// so including both <c>Posted</c> and <c>Reversed</c> headers nets them to
/// zero.</para>
///
/// <para><b>Phase-1 margin caveat.</b> COGS is not posted until Phase 2, so the
/// P&amp;L's gross margin — and the balance sheet's current-year-earnings derived
/// from it — is incomplete. Both outputs carry <c>CogsPosted = false</c> and a
/// caveat string. This ties to <c>CAP-RPT-FINANCIALS</c> (default OFF until COGS
/// posting is live — §6 sequencing note, §10).</para>
/// </summary>
public sealed class FinancialStatementService(AppDbContext db, IClock clock) : IFinancialStatementService
{
    // CogsPosted is derived per-book from the ledger (DeriveCogsPostedAsync): true once any COGS-account
    // line has posted (the Phase-2 STAGE B inventory→COGS relief at the sale). While no COGS has posted,
    // statements carry the incomplete-margin caveat so the limitation travels with the data.
    private const string MarginCaveatText =
        "Gross margin is INCOMPLETE: Cost of Goods Sold (COGS) is not posted yet " +
        "(arrives in Phase 2). Revenue and operating expense are reflected; the " +
        "inventory→COGS relief at the sale is not, so gross-margin and net-income " +
        "figures understate cost. This report is gated behind CAP-RPT-FINANCIALS, " +
        "which stays OFF until COGS posting is live.";

    /// <summary>Flat projection of one posted line for in-memory statement aggregation.</summary>
    private sealed class StatementLineRow
    {
        public int GlAccountId { get; init; }
        public string AccountNumber { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
        public AccountType AccountType { get; init; }
        public decimal Debit { get; init; }
        public decimal Credit { get; init; }
        public decimal FunctionalAmount { get; init; }
    }

    public async Task<ProfitAndLoss> GetProfitAndLossAsync(
        int bookId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken ct = default)
    {
        var rows = await ProjectLinesAsync(
            bookId,
            fromDate,
            toDate,
            type => type == AccountType.Income || type == AccountType.Expense,
            ct,
            excludeYearEndClose: true); // a closed year still reports its real revenue/expense

        // Income is credit-normal (Cr − Dr); Expense is debit-normal (Dr − Cr).
        // Each account's signed amount nets in its statement direction so a contra
        // account (e.g. Sales Returns — an Income account with a debit normal
        // balance) naturally reduces revenue.
        var income = AggregateByAccount(
            rows.Where(r => r.AccountType == AccountType.Income),
            CreditNormalAmount)
            .Select(x => new ProfitAndLossLine
            {
                GlAccountId = x.GlAccountId,
                AccountNumber = x.AccountNumber,
                AccountName = x.AccountName,
                Amount = x.Amount,
            })
            .ToList();

        var expense = AggregateByAccount(
            rows.Where(r => r.AccountType == AccountType.Expense),
            DebitNormalAmount)
            .Select(x => new ProfitAndLossLine
            {
                GlAccountId = x.GlAccountId,
                AccountNumber = x.AccountNumber,
                AccountName = x.AccountName,
                Amount = x.Amount,
            })
            .ToList();

        var cogsPosted = await DeriveCogsPostedAsync(bookId, fromDate, toDate, ct);

        return new ProfitAndLoss
        {
            BookId = bookId,
            FromDate = fromDate,
            ToDate = toDate,
            Income = income,
            Expense = expense,
            TotalIncome = income.Sum(l => l.Amount),
            TotalExpense = expense.Sum(l => l.Amount),
            CogsPosted = cogsPosted,
            MarginCaveat = cogsPosted ? string.Empty : MarginCaveatText,
        };
    }

    public async Task<BalanceSheet> GetBalanceSheetAsync(
        int bookId,
        DateOnly? asOfDate = null,
        CancellationToken ct = default)
    {
        var asOf = asOfDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // Balance-sheet accounts: all activity dated on/before the as-of date
        // (cumulative since inception — these are permanent accounts).
        var rows = await ProjectLinesAsync(
            bookId,
            fromDate: null,
            toDate: asOf,
            type => type == AccountType.Asset
                 || type == AccountType.Liability
                 || type == AccountType.Equity,
            ct);

        var assets = AggregateByAccount(
            rows.Where(r => r.AccountType == AccountType.Asset),
            DebitNormalAmount)
            .Select(ToBalanceSheetLine)
            .ToList();

        var liabilities = AggregateByAccount(
            rows.Where(r => r.AccountType == AccountType.Liability),
            CreditNormalAmount)
            .Select(ToBalanceSheetLine)
            .ToList();

        var equity = AggregateByAccount(
            rows.Where(r => r.AccountType == AccountType.Equity),
            CreditNormalAmount)
            .Select(ToBalanceSheetLine)
            .ToList();

        var currentYearEarnings = await ComputeCurrentYearEarningsAsync(bookId, asOf, ct);
        var cogsPosted = await DeriveCogsPostedAsync(bookId, fromDate: null, toDate: asOf, ct);

        return new BalanceSheet
        {
            BookId = bookId,
            AsOfDate = asOf,
            Assets = assets,
            Liabilities = liabilities,
            Equity = equity,
            TotalAssets = assets.Sum(l => l.Amount),
            TotalLiabilities = liabilities.Sum(l => l.Amount),
            TotalEquityPosted = equity.Sum(l => l.Amount),
            CurrentYearEarnings = currentYearEarnings,
            CogsPosted = cogsPosted,
            MarginCaveat = cogsPosted ? string.Empty : MarginCaveatText,
        };
    }

    /// <summary>
    /// Net income earned within the current fiscal year up to the as-of date —
    /// the interim equity adjustment that makes the balance sheet balance before
    /// the Phase-3 year-end Retained-Earnings roll. We resolve the fiscal year
    /// whose [StartDate, EndDate] contains the as-of date (filter-immune), then
    /// sum Income − Expense over [fiscalYearStart, asOf]. Returns 0 when no fiscal
    /// year covers the date (nothing to roll into earnings yet).
    /// </summary>
    private async Task<decimal> ComputeCurrentYearEarningsAsync(
        int bookId, DateOnly asOf, CancellationToken ct)
    {
        var fiscalYear = await db.FiscalYears
            .IgnoreQueryFilters()
            .Where(fy => fy.BookId == bookId && fy.StartDate <= asOf && fy.EndDate >= asOf)
            .OrderByDescending(fy => fy.StartDate)
            .Select(fy => new { fy.StartDate, fy.Status })
            .FirstOrDefaultAsync(ct);

        if (fiscalYear is null)
            return 0m;

        // A CLOSED year's earnings have already been rolled into the Retained-Earnings account by the
        // year-end close, so the interim adjustment is zero (else the balance sheet double-counts).
        if (fiscalYear.Status == FiscalYearStatus.Closed)
            return 0m;

        var pnlRows = await ProjectLinesAsync(
            bookId,
            fromDate: fiscalYear.StartDate,
            toDate: asOf,
            type => type == AccountType.Income || type == AccountType.Expense,
            ct,
            excludeYearEndClose: true);

        var income = pnlRows.Where(r => r.AccountType == AccountType.Income)
            .Sum(CreditNormalAmount);
        var expense = pnlRows.Where(r => r.AccountType == AccountType.Expense)
            .Sum(DebitNormalAmount);

        return income - expense;
    }

    /// <summary>
    /// Filter-immune projection of posted lines for the book, restricted to the
    /// supplied account-type predicate and date window. Pulls raw rows and
    /// aggregates in memory so the signing arithmetic is provider-agnostic
    /// (InMemory can't express the per-account net in SQL cleanly) and provably
    /// correct, mirroring <see cref="ArAgingService"/>.
    /// </summary>
    /// <summary>
    /// True when there is net COGS activity in the report window — the Phase-2 STAGE B inventory→COGS
    /// relief at the sale. Derived from the ledger (not from CAP-ACCT-FULLGL, which only means posting is
    /// enabled, not that COGS was recorded). <b>Window-scoped</b> so a P&amp;L for a period with revenue
    /// but no COGS-in-window keeps the incomplete-margin caveat (pass <c>null</c>/asOf for the cumulative
    /// balance sheet). Nets Dr−Cr over Posted+Reversed so a posted-then-reversed COGS reads as not-live,
    /// and resolves the full SET of COGS-keyed accounts so a future scoped rule isn't silently missed.
    /// </summary>
    private async Task<bool> DeriveCogsPostedAsync(int bookId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        var cogsAccountIds = await db.AccountDeterminationRules
            .Where(r => r.BookId == bookId && r.Key == "COGS")
            .Select(r => r.GlAccountId)
            .ToListAsync(ct);

        if (cogsAccountIds.Count == 0)
            return false;

        var net = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             where entry.BookId == bookId
                 && cogsAccountIds.Contains(line.GlAccountId)
                 && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
                 && (fromDate == null || entry.EntryDate >= fromDate)
                 && (toDate == null || entry.EntryDate <= toDate)
             select line.Debit - line.Credit)
            .SumAsync(ct);

        return net != 0m;
    }

    private async Task<List<StatementLineRow>> ProjectLinesAsync(
        int bookId,
        DateOnly? fromDate,
        DateOnly? toDate,
        Func<AccountType, bool> typeFilter,
        CancellationToken ct,
        bool excludeYearEndClose = false)
    {
        var raw = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters()
                 on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters()
                 on line.GlAccountId equals account.Id
             where entry.BookId == bookId
                 && (entry.Status == JournalEntryStatus.Posted
                     || entry.Status == JournalEntryStatus.Reversed)
                 // The year-end RE roll zeroes the P&L accounts into Retained Earnings. Excluding it from the
                 // income statement keeps a CLOSED year's revenue/expense reportable (the roll lives only in
                 // the equity/RE projection, which passes excludeYearEndClose=false).
                 && (!excludeYearEndClose || entry.SourceType != "YearEndClose")
                 && (fromDate == null || entry.EntryDate >= fromDate)
                 && (toDate == null || entry.EntryDate <= toDate)
             select new StatementLineRow
             {
                 GlAccountId = account.Id,
                 AccountNumber = account.AccountNumber,
                 AccountName = account.Name,
                 AccountType = account.AccountType,
                 Debit = line.Debit,
                 Credit = line.Credit,
                 FunctionalAmount = line.FunctionalAmount,
             })
            .ToListAsync(ct);

        return raw.Where(r => typeFilter(r.AccountType)).ToList();
    }

    /// <summary>
    /// Groups the projected lines by account, applies the supplied signing
    /// function, and drops accounts that net to zero (no activity to show). Ordered
    /// by account number for a stable statement layout.
    /// </summary>
    private static IEnumerable<(int GlAccountId, string AccountNumber, string AccountName, decimal Amount)>
        AggregateByAccount(
            IEnumerable<StatementLineRow> rows,
            Func<StatementLineRow, decimal> sign)
        => rows
            .GroupBy(r => new { r.GlAccountId, r.AccountNumber, r.AccountName })
            .Select(g => (
                g.Key.GlAccountId,
                g.Key.AccountNumber,
                g.Key.AccountName,
                Amount: g.Sum(sign)))
            .Where(x => x.Amount != 0m)
            .OrderBy(x => x.AccountNumber, StringComparer.Ordinal);

    /// <summary>Debit-normal net contribution of a line: positive when on the debit side.</summary>
    private static decimal DebitNormalAmount(StatementLineRow r)
        => r.Debit > 0 ? r.FunctionalAmount : -r.FunctionalAmount;

    /// <summary>Credit-normal net contribution of a line: positive when on the credit side.</summary>
    private static decimal CreditNormalAmount(StatementLineRow r)
        => r.Credit > 0 ? r.FunctionalAmount : -r.FunctionalAmount;

    private static BalanceSheetLine ToBalanceSheetLine(
        (int GlAccountId, string AccountNumber, string AccountName, decimal Amount) x)
        => new()
        {
            GlAccountId = x.GlAccountId,
            AccountNumber = x.AccountNumber,
            AccountName = x.AccountName,
            Amount = x.Amount,
        };
}
