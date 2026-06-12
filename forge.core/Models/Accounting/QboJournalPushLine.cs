namespace Forge.Core.Models.Accounting;

/// <summary>
/// One line of the QB-001 summary journal entry pushed to QuickBooks Online:
/// a one-sided amount against a mapped QBO account ref.
/// </summary>
public record QboJournalPushLine(
    string QboAccountId,
    bool IsDebit,
    decimal Amount,
    string? Description);
