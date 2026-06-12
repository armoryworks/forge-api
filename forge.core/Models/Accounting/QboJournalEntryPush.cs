namespace Forge.Core.Models.Accounting;

/// <summary>
/// The QB-001 one-way journal-summary push payload: ONE balanced QuickBooks
/// Online JournalEntry for the period (total debits == total credits by
/// construction — the lines are the per-account period nets).
/// </summary>
public record QboJournalEntryPush(
    DateOnly TxnDate,
    string Memo,
    IReadOnlyList<QboJournalPushLine> Lines);
