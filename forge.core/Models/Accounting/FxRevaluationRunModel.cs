namespace Forge.Core.Models.Accounting;

/// <summary>
/// A posted unrealized FX revaluation entry: its currency, as-of date, net adjustment (positive = gain) and
/// whether it has been reversed (normally by the next period's auto-reversal).
/// </summary>
public sealed record FxRevaluationRunModel(
    long JournalEntryId,
    long EntryNumber,
    DateOnly AsOf,
    int CurrencyId,
    string? CurrencyCode,
    decimal Adjustment,
    string? Memo,
    bool Reversed,
    DateTimeOffset? PostedAt);
