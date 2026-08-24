using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Creates or updates a single budget line (master data behind budget-vs-actual).
/// The (book, account, fiscal year, month) tuple is the identity: an existing live
/// row is updated in place, otherwise a new one is created — mirroring the filtered
/// unique index <c>ux_acct_budgets_book_account_year_period</c>. CAP-ACCT-FULLGL
/// gated. The auditable-entity change tracker emits the ActivityLog row (created /
/// field-changed) on save, so budgets carry the same audit trail as other
/// definitional accounting data.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record UpsertBudgetCommand(UpsertBudgetRequestModel Model) : IRequest<BudgetLineModel>;

public class UpsertBudgetValidator : AbstractValidator<UpsertBudgetCommand>
{
    public UpsertBudgetValidator()
    {
        RuleFor(x => x.Model.BookId).GreaterThan(0);
        RuleFor(x => x.Model.GlAccountId).GreaterThan(0);
        RuleFor(x => x.Model.FiscalYear).InclusiveBetween(1900, 9999);
        RuleFor(x => x.Model.PeriodMonth).InclusiveBetween(1, 12)
            .When(x => x.Model.PeriodMonth is not null);
    }
}

public class UpsertBudgetHandler(AppDbContext db) : IRequestHandler<UpsertBudgetCommand, BudgetLineModel>
{
    public async Task<BudgetLineModel> Handle(UpsertBudgetCommand request, CancellationToken ct)
    {
        var m = request.Model;

        var account = await db.GlAccounts
            .FirstOrDefaultAsync(a => a.Id == m.GlAccountId && a.BookId == m.BookId, ct)
            ?? throw new InvalidOperationException("GL account not found in this book.");

        var budget = await db.AcctBudgets.FirstOrDefaultAsync(
            b => b.BookId == m.BookId
              && b.GlAccountId == m.GlAccountId
              && b.FiscalYear == m.FiscalYear
              && b.PeriodMonth == m.PeriodMonth,
            ct);

        if (budget is null)
        {
            budget = new AcctBudget
            {
                BookId = m.BookId,
                GlAccountId = m.GlAccountId,
                FiscalYear = m.FiscalYear,
                PeriodMonth = m.PeriodMonth,
                Amount = m.Amount,
            };
            db.AcctBudgets.Add(budget);
        }
        else
        {
            budget.Amount = m.Amount;
        }

        await db.SaveChangesAsync(ct);

        return new BudgetLineModel(
            budget.Id, budget.BookId, budget.GlAccountId,
            account.AccountNumber, account.Name,
            budget.FiscalYear, budget.PeriodMonth, budget.Amount);
    }
}
