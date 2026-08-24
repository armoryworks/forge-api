using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Budget-vs-actual P&amp;L comparison for a book over a fiscal year (or a single
/// month). Actuals are read from <see cref="IFinancialStatementService"/> — the
/// same filter-immune ledger projection the Profit &amp; Loss and trial balance use
/// — so nothing re-implements the ledger query. Budgets come from the stored
/// <c>acct_budgets</c> rows for the matching scope (month-null for a full year, the
/// exact month otherwise). CAP-ACCT-FULLGL gated.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetBudgetVsActualQuery(int BookId, int FiscalYear, int? PeriodMonth = null)
    : IRequest<BudgetVsActual>;

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

        // Budgets for the requested scope: full-year rows (month null) or the exact
        // month. Summed defensively though the unique index yields one row per slot.
        var budgetRows = await db.AcctBudgets.AsNoTracking()
            .Where(b => b.BookId == request.BookId
                     && b.FiscalYear == request.FiscalYear
                     && b.PeriodMonth == request.PeriodMonth)
            .GroupBy(b => b.GlAccountId)
            .Select(g => new { GlAccountId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        var budgetByAccount = budgetRows.ToDictionary(b => b.GlAccountId, b => b.Amount);

        // Account number/name for budgeted accounts that had no ledger activity
        // (absent from the P&L lines) — one lookup, no per-row query.
        var missingIds = budgetByAccount.Keys.Where(id => !actualByAccount.ContainsKey(id)).ToList();
        var missingAccounts = missingIds.Count == 0
            ? []
            : await db.GlAccounts.AsNoTracking()
                .Where(a => missingIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AccountNumber, a.Name })
                .ToDictionaryAsync(a => a.Id, a => (a.AccountNumber, a.Name), ct);

        var lines = new List<BudgetVsActualLine>();
        foreach (var accountId in actualByAccount.Keys.Union(budgetByAccount.Keys))
        {
            var hasActual = actualByAccount.TryGetValue(accountId, out var actual);
            var number = hasActual ? actual.AccountNumber
                : missingAccounts.TryGetValue(accountId, out var acct) ? acct.AccountNumber : string.Empty;
            var name = hasActual ? actual.AccountName
                : missingAccounts.TryGetValue(accountId, out var acct2) ? acct2.Name : string.Empty;

            lines.Add(new BudgetVsActualLine
            {
                GlAccountId = accountId,
                AccountNumber = number,
                AccountName = name,
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
        };
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

        var monthStart = new DateOnly(fiscalYear, month.Value, 1);
        return (monthStart, monthStart.AddMonths(1).AddDays(-1));
    }
}
