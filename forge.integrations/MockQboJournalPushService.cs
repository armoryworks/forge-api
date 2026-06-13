using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Integrations;

/// <summary>
/// QB-001 mock — logs the would-be QuickBooks JournalEntry and returns a
/// deterministic fake doc id. Registered under <c>MockIntegrations=true</c>
/// exactly like the other integration mocks.
/// </summary>
public class MockQboJournalPushService(ILogger<MockQboJournalPushService> logger) : IQboJournalPushService
{
    public Task<string> PushJournalEntryAsync(QboJournalEntryPush entry, CancellationToken ct)
    {
        var totalDebit = entry.Lines.Where(l => l.IsDebit).Sum(l => l.Amount);
        var docId = $"MOCK-QBO-JE-{entry.TxnDate:yyyyMMdd}";

        logger.LogInformation(
            "[MockQbo] PushJournalEntry \"{Memo}\" on {TxnDate}: {LineCount} lines, {TotalDebit:C} Dr → {DocId}",
            entry.Memo, entry.TxnDate, entry.Lines.Count, totalDebit, docId);

        return Task.FromResult(docId);
    }
}
