namespace Forge.Core.Models.Accounting;

/// <summary>
/// Budget-vs-actual P&amp;L comparison for a book over a fiscal year (or a single
/// month within it). Actuals come from the same filter-immune ledger projection
/// the Profit &amp; Loss uses (<see cref="ProfitAndLoss"/>), so budget and actual
/// are struck on identical numbers. Each <see cref="BudgetVsActualLine"/> pairs a
/// GL account's budget and actual; a budgeted account with no activity reads
/// actual 0, an account with activity but no budget reads budget 0. Totals'
/// variance getters reuse <see cref="StatementVariance"/>.
/// </summary>
public sealed class BudgetVsActual
{
    public int BookId { get; init; }

    public int FiscalYear { get; init; }

    /// <summary><c>null</c> = full fiscal year; 1..12 = a single month within it.</summary>
    public int? PeriodMonth { get; init; }

    /// <summary>Inclusive start of the resolved reporting window (for display).</summary>
    public DateOnly FromDate { get; init; }

    /// <summary>Inclusive end of the resolved reporting window (for display).</summary>
    public DateOnly ToDate { get; init; }

    public IReadOnlyList<BudgetVsActualLine> Lines { get; init; } = [];

    public decimal TotalBudget { get; init; }
    public decimal TotalActual { get; init; }

    public decimal? TotalVariance => StatementVariance.Variance(TotalActual, TotalBudget);
    public decimal? TotalVariancePercent => StatementVariance.Percent(TotalActual, TotalBudget);
}
