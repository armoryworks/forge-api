namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ BANK-001 — one Posted cash journal line a statement line can be matched to by hand, with how far it is
/// from the statement line in days and in amount (both zero for an exact fit).
/// </summary>
public record BankStatementMatchCandidateModel(
    long JournalLineId,
    long JournalEntryId,
    long EntryNumber,
    string EntryDate,
    decimal Amount,
    string? Memo,
    string? Description,
    string Source,
    string? SourceType,
    int DaysApart,
    decimal AmountDifference);
