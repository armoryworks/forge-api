using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Lists a book's budget lines for a fiscal year (full-year and monthly rows),
/// joined to their GL account for display. CAP-ACCT-FULLGL gated. Read-only, so
/// the query is untracked.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record ListBudgetsQuery(int BookId, int FiscalYear) : IRequest<IReadOnlyList<BudgetLineModel>>;

public class ListBudgetsHandler(AppDbContext db)
    : IRequestHandler<ListBudgetsQuery, IReadOnlyList<BudgetLineModel>>
{
    public async Task<IReadOnlyList<BudgetLineModel>> Handle(ListBudgetsQuery request, CancellationToken ct)
    {
        var rows = await
            (from budget in db.AcctBudgets.AsNoTracking()
             join account in db.GlAccounts.AsNoTracking() on budget.GlAccountId equals account.Id
             where budget.BookId == request.BookId && budget.FiscalYear == request.FiscalYear
             orderby account.AccountNumber, budget.PeriodMonth
             select new BudgetLineModel(
                 budget.Id, budget.BookId, budget.GlAccountId,
                 account.AccountNumber, account.Name,
                 budget.FiscalYear, budget.PeriodMonth, budget.Amount))
            .ToListAsync(ct);

        return rows;
    }
}
