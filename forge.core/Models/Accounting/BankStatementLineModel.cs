namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ BANK-001 — one staged statement line with its (suggested/confirmed) GL counterpart for
/// side-by-side review.
/// </summary>
public record BankStatementLineModel(
    long Id,
    string PostedDate,        // yyyy-MM-dd
    decimal Amount,           // signed: + deposit / − withdrawal
    string Description,
    string MatchStatus,
    long? MatchedJournalLineId,
    long? MatchedEntryNumber,
    string? MatchedEntryDate, // yyyy-MM-dd
    string? MatchedMemo,
    DateTimeOffset? ConfirmedAt);
