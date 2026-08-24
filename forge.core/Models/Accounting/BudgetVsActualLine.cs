namespace Forge.Core.Models.Accounting;

/// <summary>
/// One GL account's budget-vs-actual comparison for the reporting window.
/// <see cref="ActualAmount"/> is the P&amp;L statement-direction amount from the
/// same ledger projection the income statement uses; <see cref="BudgetAmount"/>
/// is the stored budget for the same scope. Variance = actual − budget (a
/// positive expense variance is over-budget); variance % divides by |budget| and
/// is <c>null</c> when the budget is zero — both delegate to
/// <see cref="StatementVariance"/> so the divide-by-zero guard lives in one place.
/// </summary>
public sealed class BudgetVsActualLine
{
    public int GlAccountId { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;

    public decimal BudgetAmount { get; init; }
    public decimal ActualAmount { get; init; }

    public decimal? Variance => StatementVariance.Variance(ActualAmount, BudgetAmount);
    public decimal? VariancePercent => StatementVariance.Percent(ActualAmount, BudgetAmount);
}
