using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Costing;
using Forge.Core.Models.Costing;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Sets (creates or replaces) a pool's budget for a costing period. The derived rate is
/// computed here for preview and re-computed authoritatively at freeze.</summary>
public record UpsertOverheadPoolBudgetCommand(
    int OverheadCostPoolId,
    int CostingPeriodId,
    decimal BudgetAmount,
    decimal BudgetDriverQty) : IRequest<OverheadPoolBudgetResponseModel>;

public class UpsertOverheadPoolBudgetValidator : AbstractValidator<UpsertOverheadPoolBudgetCommand>
{
    public UpsertOverheadPoolBudgetValidator()
    {
        RuleFor(x => x.BudgetAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BudgetDriverQty).GreaterThan(0)
            .WithMessage("Budget driver quantity must be positive (it divides the budget into a rate).");
    }
}

public class UpsertOverheadPoolBudgetHandler(AppDbContext db)
    : IRequestHandler<UpsertOverheadPoolBudgetCommand, OverheadPoolBudgetResponseModel>
{
    public async Task<OverheadPoolBudgetResponseModel> Handle(UpsertOverheadPoolBudgetCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await db.AcquireAdvisoryLockAsync(
            "overhead-pool-budgets.upsert", $"{request.OverheadCostPoolId}:{request.CostingPeriodId}", ct);

        var budget = await db.OverheadPoolBudgets
            .Where(b => b.OverheadCostPoolId == request.OverheadCostPoolId && b.CostingPeriodId == request.CostingPeriodId)
            .OrderByDescending(b => b.UpdatedAt)
            .ThenByDescending(b => b.Id)
            .FirstOrDefaultAsync(ct);

        if (budget is null)
        {
            budget = new OverheadPoolBudget
            {
                OverheadCostPoolId = request.OverheadCostPoolId,
                CostingPeriodId = request.CostingPeriodId,
            };
            db.OverheadPoolBudgets.Add(budget);
        }

        budget.BudgetAmount = request.BudgetAmount;
        budget.BudgetDriverQty = request.BudgetDriverQty;
        budget.DerivedRate = request.BudgetDriverQty != 0 ? request.BudgetAmount / request.BudgetDriverQty : 0;

        db.LogActivityAt("budget-set",
            $"Set overhead budget: {budget.BudgetAmount:0.00} over {budget.BudgetDriverQty:0.####} → rate {budget.DerivedRate:0.######}",
            ("OverheadCostPool", budget.OverheadCostPoolId), ("CostingPeriod", budget.CostingPeriodId));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);

        return new OverheadPoolBudgetResponseModel(
            budget.Id, budget.OverheadCostPoolId, budget.CostingPeriodId,
            budget.BudgetAmount, budget.BudgetDriverQty, budget.DerivedRate);
    }
}
