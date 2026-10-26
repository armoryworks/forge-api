namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ Phase-4b — result of a period-end unrealized FX revaluation. Re-measures the net foreign monetary
/// position (foreign cash plus open receivables and payables in the currency) to a new rate; the functional
/// carrying adjustment posts to FX_REVALUATION / FX_GAIN|FX_LOSS and auto-reverses next period (unrealized —
/// the realized gain or loss happens on settlement). <c>Rate</c> is the rate used and
/// <c>CarryingFunctional</c> the functional value before the adjustment; <c>JournalEntryId</c> is null for a
/// preview or when nothing needed to post.
/// </summary>
public sealed record FxRevaluationResult(
    int BookId,
    int CurrencyId,
    DateOnly AsOf,
    decimal NetForeignPosition,
    decimal Adjustment,
    long? JournalEntryId,
    decimal Rate = 0m,
    decimal CarryingFunctional = 0m,
    string? CurrencyCode = null);
