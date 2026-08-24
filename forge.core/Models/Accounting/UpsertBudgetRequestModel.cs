namespace Forge.Core.Models.Accounting;

/// <summary>
/// Create-or-update payload for a budget line. The (book, account, year, month)
/// tuple identifies the row: an existing live row with the same tuple is updated
/// in place, otherwise a new one is created. <c>PeriodMonth</c> null = full-year.
/// </summary>
public sealed record UpsertBudgetRequestModel(
    int BookId,
    int GlAccountId,
    int FiscalYear,
    int? PeriodMonth,
    decimal Amount);
