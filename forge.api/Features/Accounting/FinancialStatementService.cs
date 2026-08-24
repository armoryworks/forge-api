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
        bool compare = false,
        DateOnly? compareFromDate = null,
        DateOnly? compareToDate = null,
        CancellationToken ct = default)
    {
        if (!compare)
            return await BuildProfitAndLossAsync(bookId, fromDate, toDate, ct);

        // Comparison requested. Resolve the CURRENT window first: an explicit
        // [fromDate, toDate] is used as-is; an open-ended default statement is
        // scoped to the current fiscal year (start → today) so there is a bounded
        // period to compare against — turning comparison on necessarily scopes an
        // otherwise all-time P&L to a period, which is what a period-over-period
        // report means. Both current and prior are built by the same core method.
        var (curFrom, curTo) = await ResolveCurrentPnlWindowAsync(bookId, fromDate, toDate, ct);
        var current = await BuildProfitAndLossAsync(bookId, curFrom, curTo, ct);

        // Resolve the PRIOR window: explicit compare bounds win; otherwise derive
        // the immediately-preceding period of equal length. If neither is possible
        // (current window not fully bounded), return the current statement with no
        // comparison rather than inventing a range.
        var (priFrom, priTo) = ResolvePriorPnlWindow(curFrom, curTo, compareFromDate, compareToDate);
        if (priFrom is null || priTo is null)
            return current;

        var prior = await BuildProfitAndLossAsync(bookId, priFrom, priTo, ct);
        return MergeProfitAndLoss(current, prior, priFrom.Value, priTo.Value);
    }

    private async Task<ProfitAndLoss> BuildProfitAndLossAsync(
        int bookId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken ct)
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
        bool compare = false,
        DateOnly? compareAsOfDate = null,
        CancellationToken ct = default)
    {
        if (!compare)
            return await BuildBalanceSheetAsync(bookId, asOfDate, ct);

        var current = await BuildBalanceSheetAsync(bookId, asOfDate, ct);

        // Prior date: explicit compareAsOfDate wins; otherwise default to the same
        // date one year earlier (the standard year-over-year balance-sheet compare).
        var priorAsOf = compareAsOfDate ?? current.AsOfDate.AddYears(-1);
        var prior = await BuildBalanceSheetAsync(bookId, priorAsOf, ct);
        return MergeBalanceSheet(current, prior, priorAsOf);
    }

    private async Task<BalanceSheet> BuildBalanceSheetAsync(
        int bookId,
        DateOnly? asOfDate,
        CancellationToken ct)
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

    // ── Comparative-period merge + window resolution ─────────────────────────

    /// <summary>
    /// Folds a prior-period <see cref="ProfitAndLoss"/> into the current one:
    /// annotates each income/expense line with its prior amount (0 when the account
    /// had no prior activity) and carries the prior totals. Accounts that appear
    /// only in the prior window surface as current-0 lines so a line that dropped
    /// to nothing period-over-period is still visible. Variance/variance% are
    /// getters on the models (StatementVariance), so nothing is computed twice.
    /// </summary>
    private static ProfitAndLoss MergeProfitAndLoss(
        ProfitAndLoss current, ProfitAndLoss prior, DateOnly compareFrom, DateOnly compareTo)
        => new()
        {
            BookId = current.BookId,
            FromDate = current.FromDate,
            ToDate = current.ToDate,
            Income = MergeLines(current.Income, prior.Income,
                (id, num, name, cur, pri) => new ProfitAndLossLine
                {
                    GlAccountId = id, AccountNumber = num, AccountName = name,
                    Amount = cur, PriorAmount = pri,
                }),
            Expense = MergeLines(current.Expense, prior.Expense,
                (id, num, name, cur, pri) => new ProfitAndLossLine
                {
                    GlAccountId = id, AccountNumber = num, AccountName = name,
                    Amount = cur, PriorAmount = pri,
                }),
            TotalIncome = current.TotalIncome,
            TotalExpense = current.TotalExpense,
            CogsPosted = current.CogsPosted,
            MarginCaveat = current.MarginCaveat,
            CompareFromDate = compareFrom,
            CompareToDate = compareTo,
            PriorTotalIncome = prior.TotalIncome,
            PriorTotalExpense = prior.TotalExpense,
            PriorNetIncome = prior.NetIncome,
        };

    /// <summary>Folds a prior-date balance sheet into the current one (see <see cref="MergeProfitAndLoss"/>).</summary>
    private static BalanceSheet MergeBalanceSheet(
        BalanceSheet current, BalanceSheet prior, DateOnly compareAsOf)
        => new()
        {
            BookId = current.BookId,
            AsOfDate = current.AsOfDate,
            Assets = MergeLines(current.Assets, prior.Assets, MakeBalanceSheetLine),
            Liabilities = MergeLines(current.Liabilities, prior.Liabilities, MakeBalanceSheetLine),
            Equity = MergeLines(current.Equity, prior.Equity, MakeBalanceSheetLine),
            TotalAssets = current.TotalAssets,
            TotalLiabilities = current.TotalLiabilities,
            TotalEquityPosted = current.TotalEquityPosted,
            CurrentYearEarnings = current.CurrentYearEarnings,
            CogsPosted = current.CogsPosted,
            MarginCaveat = current.MarginCaveat,
            CompareAsOfDate = compareAsOf,
            PriorTotalAssets = prior.TotalAssets,
            PriorTotalLiabilities = prior.TotalLiabilities,
            PriorCurrentYearEarnings = prior.CurrentYearEarnings,
            PriorTotalEquityWithEarnings = prior.TotalEquityWithEarnings,
            PriorTotalLiabilitiesAndEquity = prior.TotalLiabilitiesAndEquity,
        };

    private static BalanceSheetLine MakeBalanceSheetLine(
        int id, string num, string name, decimal cur, decimal prior)
        => new()
        {
            GlAccountId = id, AccountNumber = num, AccountName = name,
            Amount = cur, PriorAmount = prior,
        };

    /// <summary>
    /// Unions two statement-line lists by account, pairing each account's current
    /// and prior amounts (0 when absent on a side) and re-ordering by account
    /// number. <paramref name="make"/> builds the concrete line type
    /// (GlAccountId, AccountNumber, AccountName, currentAmount, priorAmount).
    /// </summary>
    private static IReadOnlyList<TLine> MergeLines<TLine>(
        IReadOnlyList<TLine> current,
        IReadOnlyList<TLine> prior,
        Func<int, string, string, decimal, decimal, TLine> make)
        where TLine : class
    {
        // Both line types share the same public surface; read via a tiny local
        // accessor so this helper stays generic without an interface on the models.
        static (int Id, string Number, string Name, decimal Amount) Read(TLine line) => line switch
        {
            ProfitAndLossLine p => (p.GlAccountId, p.AccountNumber, p.AccountName, p.Amount),
            BalanceSheetLine b => (b.GlAccountId, b.AccountNumber, b.AccountName, b.Amount),
            _ => throw new NotSupportedException($"Unsupported statement line type {typeof(TLine)}"),
        };

        var priorAmountById = prior.ToDictionary(l => Read(l).Id, l => Read(l).Amount);
        var seen = new HashSet<int>();
        var merged = new List<TLine>(current.Count + prior.Count);

        foreach (var line in current)
        {
            var (id, number, name, amount) = Read(line);
            seen.Add(id);
            var priorAmount = priorAmountById.TryGetValue(id, out var p) ? p : 0m;
            merged.Add(make(id, number, name, amount, priorAmount));
        }

        foreach (var line in prior)
        {
            var (id, number, name, amount) = Read(line);
            if (!seen.Add(id))
                continue; // already paired from the current side
            merged.Add(make(id, number, name, 0m, amount));
        }

        return merged.OrderBy(l => Read(l).Number, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Resolves the CURRENT P&amp;L window for a comparison. Explicit bounds are used
    /// verbatim; an open-ended default statement is scoped to the fiscal year that
    /// contains today (start → today) so there is a bounded period to compare. When
    /// no fiscal year covers today the bounds pass through unchanged (comparison is
    /// then skipped upstream).
    /// </summary>
    private async Task<(DateOnly? From, DateOnly? To)> ResolveCurrentPnlWindowAsync(
        int bookId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        if (fromDate is not null && toDate is not null)
            return (fromDate, toDate);

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var fyStart = await db.FiscalYears
            .IgnoreQueryFilters()
            .Where(fy => fy.BookId == bookId && fy.StartDate <= today && fy.EndDate >= today)
            .OrderByDescending(fy => fy.StartDate)
            .Select(fy => (DateOnly?)fy.StartDate)
            .FirstOrDefaultAsync(ct);

        return fyStart is null ? (fromDate, toDate) : (fyStart, today);
    }

    /// <summary>
    /// Resolves the PRIOR P&amp;L window. Explicit compare bounds win; otherwise the
    /// immediately-preceding period of equal (inclusive) length is derived: prior
    /// ends the day before the current window starts and spans the same number of
    /// days. Returns nulls when neither is possible (current window unbounded).
    /// </summary>
    private static (DateOnly? From, DateOnly? To) ResolvePriorPnlWindow(
        DateOnly? curFrom, DateOnly? curTo, DateOnly? compareFrom, DateOnly? compareTo)
    {
        if (compareFrom is not null && compareTo is not null)
            return (compareFrom, compareTo);

        if (curFrom is null || curTo is null)
            return (null, null);

        var priorTo = curFrom.Value.AddDays(-1);
        var lengthDays = curTo.Value.DayNumber - curFrom.Value.DayNumber; // inclusive span − 1
        var priorFrom = priorTo.AddDays(-lengthDays);
        return (priorFrom, priorTo);
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
