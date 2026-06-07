namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ Phase-4b — result of a period-end unrealized FX revaluation. Re-measures the net foreign monetary
/// position (cash + AR/AP control in the foreign currency) to a new rate; the functional carrying adjustment
/// posts to FX_REVALUATION / FX_GAIN|FX_LOSS and auto-reverses next period (unrealized — the realized gain or
/// loss happens on settlement).
/// </summary>
public sealed record FxRevaluationResult(
    int BookId,
    int CurrencyId,
    DateOnly AsOf,
    decimal NetForeignPosition,
    decimal Adjustment,
    long? JournalEntryId);
