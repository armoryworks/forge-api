using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// QB-001 — the one-way downstream channel that delivers a balanced summary
/// JournalEntry to QuickBooks Online for the CPA. QuickBooks is NEVER the
/// system of record: this seam only writes, never reads back. Real
/// implementation rides the same stored OAuth connection as
/// <c>QuickBooksAccountingService</c>; the mock logs and returns a fake doc id
/// (registered under MockIntegrations like every other integration).
/// </summary>
public interface IQboJournalPushService
{
    /// <summary>
    /// Push one balanced journal entry; returns the QBO JournalEntry doc id.
    /// Throws <see cref="InvalidOperationException"/> when QuickBooks is not
    /// connected or rejects the entry (→ 409 at the HTTP edge).
    /// </summary>
    Task<string> PushJournalEntryAsync(QboJournalEntryPush entry, CancellationToken ct);
}
