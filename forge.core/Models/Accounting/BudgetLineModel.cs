namespace Forge.Core.Models.Accounting;

/// <summary>
/// One stored budget line for a GL account in a fiscal year (full-year when
/// <see cref="PeriodMonth"/> is <c>null</c>, otherwise that month). Carries the
/// account's number/name for display so the list needs no second lookup.
/// </summary>
public sealed record BudgetLineModel(
    int Id,
    int BookId,
    int GlAccountId,
    string AccountNumber,
    string AccountName,
    int FiscalYear,
    int? PeriodMonth,
    decimal Amount);
