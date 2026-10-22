using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Budget-vs-actual P&amp;L comparison for a book over a fiscal year (or a single
/// month). Actuals are read from <see cref="IFinancialStatementService"/> — the
/// same filter-immune ledger projection the Profit &amp; Loss and trial balance use
/// — so nothing re-implements the ledger query. Budgets come from the stored
/// <c>acct_budgets</c> rows: a full year reads the annual row (month null), or the sum
/// of the account's monthly rows when it has no annual row; a month reads its own
/// monthly row, else spreads what is left of the annual amount (after the explicit
/// monthly rows) evenly over the fiscal periods without one, the rounding remainder on
/// the last, so the months always add up to the year. CAP-ACCT-FULLGL gated.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetBudgetVsActualQuery(int BookId, int FiscalYear, int? PeriodMonth = null)
    : IRequest<BudgetVsActual>;

public class GetBudgetVsActualValidator : AbstractValidator<GetBudgetVsActualQuery>
{
    public GetBudgetVsActualValidator()
    {
        RuleFor(x => x.BookId).GreaterThan(0);
        RuleFor(x => x.FiscalYear).InclusiveBetween(1900, 9999);
        RuleFor(x => x.PeriodMonth).InclusiveBetween(1, 13)
            .When(x => x.PeriodMonth is not null);
    }
}

public class GetBudgetVsActualHandler(AppDbContext db, IFinancialStatementService statements)
    : IRequestHandler<GetBudgetVsActualQuery, BudgetVsActual>
{
    public async Task<BudgetVsActual> Handle(GetBudgetVsActualQuery request, CancellationToken ct)
    {
        var (from, to) = await ResolveWindowAsync(request.BookId, request.FiscalYear, request.PeriodMonth, ct);

        // Actuals from the shared ledger projection (Income + Expense lines, signed
        // in statement direction). This is the P&L read seam — not a duplicated query.
        var pnl = await statements.GetProfitAndLossAsync(request.BookId, from, to, ct: ct);
        var actualByAccount = pnl.Income.Concat(pnl.Expense)
            .ToDictionary(
                l => l.GlAccountId,
                l => (l.AccountNumber, l.AccountName, l.Amount));
        var incomeIds = pnl.Income.Select(l => l.GlAccountId).ToHashSet();

        var budgetRows = await db.AcctBudgets.AsNoTracking()
            .Where(b => b.BookId == request.BookId && b.FiscalYear == request.FiscalYear)
            .Select(b => new { b.GlAccountId, b.PeriodMonth, b.Amount })
            .ToListAsync(ct);
        var periodCount = request.PeriodMonth is null
            ? 0
            : await CountFiscalPeriodsAsync(request.BookId, request.FiscalYear, ct);
        var budgetByAccount = budgetRows
            .GroupBy(b => b.GlAccountId)
            .ToDictionary(
                g => g.Key,
                g => ResolveBudget(
                    g.Any(b => b.PeriodMonth is null) ? g.Where(b => b.PeriodMonth is null).Sum(b => b.Amount) : null,
                    g.Where(b => b.PeriodMonth is not null)
                        .GroupBy(b => b.PeriodMonth!.Value)
                        .ToDictionary(m => m.Key, m => m.Sum(x => x.Amount)),
                    request.PeriodMonth,
                    periodCount))
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!.Value);

        // Account number/name for budgeted accounts that had no ledger activity
        // (absent from the P&L lines) — one lookup, no per-row query.
        var missingIds = budgetByAccount.Keys.Where(id => !actualByAccount.ContainsKey(id)).ToList();
        var missingAccounts = missingIds.Count == 0
            ? []
            : await db.GlAccounts.AsNoTracking()
                .Where(a => missingIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AccountNumber, a.Name, a.AccountType })
                .ToDictionaryAsync(a => a.Id, a => (a.AccountNumber, a.Name, a.AccountType), ct);

        var lines = new List<BudgetVsActualLine>();
        foreach (var accountId in actualByAccount.Keys.Union(budgetByAccount.Keys))
        {
            var hasActual = actualByAccount.TryGetValue(accountId, out var actual);
            var number = hasActual ? actual.AccountNumber
                : missingAccounts.TryGetValue(accountId, out var acct) ? acct.AccountNumber : string.Empty;
            var name = hasActual ? actual.AccountName
                : missingAccounts.TryGetValue(accountId, out var acct2) ? acct2.Name : string.Empty;
            var isIncome = hasActual
                ? incomeIds.Contains(accountId)
                : missingAccounts.TryGetValue(accountId, out var acct3) && acct3.AccountType == AccountType.Income;

            lines.Add(new BudgetVsActualLine
            {
                GlAccountId = accountId,
                AccountNumber = number,
                AccountName = name,
                IsIncome = isIncome,
                ActualAmount = hasActual ? actual.Amount : 0m,
                BudgetAmount = budgetByAccount.TryGetValue(accountId, out var budget) ? budget : 0m,
            });
        }

        var ordered = lines.OrderBy(l => l.AccountNumber, StringComparer.Ordinal).ToList();

        return new BudgetVsActual
        {
            BookId = request.BookId,
            FiscalYear = request.FiscalYear,
            PeriodMonth = request.PeriodMonth,
            FromDate = from,
            ToDate = to,
            Lines = ordered,
            TotalBudget = ordered.Sum(l => l.BudgetAmount),
            TotalActual = ordered.Sum(l => l.ActualAmount),
            TotalBudgetIncome = ordered.Where(l => l.IsIncome).Sum(l => l.BudgetAmount),
            TotalBudgetExpense = ordered.Where(l => !l.IsIncome).Sum(l => l.BudgetAmount),
            TotalActualIncome = ordered.Where(l => l.IsIncome).Sum(l => l.ActualAmount),
            TotalActualExpense = ordered.Where(l => !l.IsIncome).Sum(l => l.ActualAmount),
        };
    }

    /// <summary>
    /// One account's budget for the scope, or null when it has no budget there. Monthly
    /// rows beat the spread of the annual amount; with no annual row a month has only
    /// its own row and the year is the sum of the monthly rows.
    /// </summary>
    internal static decimal? ResolveBudget(
        decimal? annual, IReadOnlyDictionary<int, decimal> monthly, int? month, int periodCount)
    {
        if (month is null)
            return annual ?? (monthly.Count > 0 ? monthly.Values.Sum() : null);

        if (monthly.TryGetValue(month.Value, out var explicitAmount))
            return explicitAmount;

        if (annual is null || month.Value < 1 || month.Value > periodCount)
            return null;

        var open = Enumerable.Range(1, periodCount).Where(p => !monthly.ContainsKey(p)).ToList();
        var residual = annual.Value - monthly.Where(m => m.Key >= 1 && m.Key <= periodCount).Sum(m => m.Value);
        var share = Math.Round(residual / open.Count, 2);
        return month.Value == open[^1] ? residual - share * (open.Count - 1) : share;
    }

    /// <summary>
    /// Number of periods in the book's fiscal year (as matched by
    /// <see cref="ResolveWindowAsync"/>), or 12 when the year has no period rows.
    /// </summary>
    private async Task<int> CountFiscalPeriodsAsync(int bookId, int fiscalYear, CancellationToken ct)
    {
        var fyId = await db.FiscalYears.AsNoTracking()
            .Where(f => f.BookId == bookId
                     && (f.StartDate.Year == fiscalYear || f.EndDate.Year == fiscalYear))
            .OrderBy(f => f.StartDate.Year == fiscalYear ? 0 : 1)
            .ThenByDescending(f => f.StartDate)
            .Select(f => (int?)f.Id)
            .FirstOrDefaultAsync(ct);

        var count = fyId is null
            ? 0
            : await db.FiscalPeriods.AsNoTracking().CountAsync(p => p.FiscalYearId == fyId.Value, ct);
        return count > 0 ? count : 12;
    }

    /// <summary>
    /// Resolves the [from, to] window for a fiscal-year (+ optional month) budget
    /// scope. Prefers the book's <c>FiscalYear</c>/<c>FiscalPeriod</c> rows so a
    /// non-calendar fiscal year lines up with the ledger; falls back to the calendar
    /// year / month when no matching fiscal-year row exists.
    /// </summary>
    private async Task<(DateOnly From, DateOnly To)> ResolveWindowAsync(
        int bookId, int fiscalYear, int? month, CancellationToken ct)
    {
        var fy = await db.FiscalYears.AsNoTracking()
            .Where(f => f.BookId == bookId
                     && (f.StartDate.Year == fiscalYear || f.EndDate.Year == fiscalYear))
            .OrderBy(f => f.StartDate.Year == fiscalYear ? 0 : 1)
            .ThenByDescending(f => f.StartDate)
            .Select(f => new { f.Id, f.StartDate, f.EndDate })
            .FirstOrDefaultAsync(ct);

        if (fy is not null)
        {
            if (month is null)
                return (fy.StartDate, fy.EndDate);

            var period = await db.FiscalPeriods.AsNoTracking()
                .Where(p => p.FiscalYearId == fy.Id && p.PeriodNumber == month)
                .Select(p => new { p.StartDate, p.EndDate })
                .FirstOrDefaultAsync(ct);

            if (period is not null)
                return (period.StartDate, period.EndDate);
        }

        if (month is null)
            return (new DateOnly(fiscalYear, 1, 1), new DateOnly(fiscalYear, 12, 31));

        if (month.Value > 12)
            throw new KeyNotFoundException($"Fiscal year {fiscalYear} has no period {month.Value}.");

        var monthStart = new DateOnly(fiscalYear, month.Value, 1);
        return (monthStart, monthStart.AddMonths(1).AddDays(-1));
    }
}
