using Forge.Core.Entities.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Shared projection of a tracked <see cref="JournalEntry"/> (with its lines loaded) to the thin
/// <see cref="ManualJournalEntryResult"/> the manual-JE feature returns — used by the create, approve, and
/// pending-list handlers so the shape stays in one place. We never return the tracked entity itself.
/// </summary>
internal static class JournalEntryResultMapper
{
    public static ManualJournalEntryResult ToManualResult(this JournalEntry entry) => new(
        entry.Id,
        entry.BookId,
        entry.EntryNumber,
        entry.EntryDate,
        entry.FiscalPeriodId,
        entry.FiscalYearId,
        entry.Status.ToString(),
        entry.Memo,
        entry.PostedBy,
        entry.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new ManualJournalLineResult(
                l.Id, l.LineNumber, l.GlAccountId, l.Debit, l.Credit, l.FunctionalAmount, l.Description))
            .ToList());
}
