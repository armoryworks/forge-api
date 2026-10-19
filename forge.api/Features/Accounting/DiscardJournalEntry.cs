using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Discards a manual journal entry that never posted — a <c>Draft</c>, which is where a rejected entry lands
/// (§5.7). The draft and its lines are deleted outright: nothing was applied to the ledger-balance read-model,
/// and the immutability interceptor leaves pre-Posted entries mutable. Its EntryNumber becomes a gap (gaps
/// are allowed). DARK behind <c>CAP-ACCT-FULLGL</c>.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record DiscardJournalEntryCommand(long EntryId) : IRequest;

public class DiscardJournalEntryHandler(AppDbContext db) : IRequestHandler<DiscardJournalEntryCommand>
{
    public async Task Handle(DiscardJournalEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == request.EntryId, cancellationToken)
            ?? throw new KeyNotFoundException($"Journal entry {request.EntryId} not found.");

        if (entry.Status != JournalEntryStatus.Draft || entry.Source != JournalSource.Manual)
            throw new InvalidOperationException(
                $"Only a manual Draft entry can be discarded; entry {request.EntryId} is {entry.Status}.");

        db.JournalLines.RemoveRange(entry.Lines);
        db.JournalEntries.Remove(entry);
        db.LogActivityAt("discarded",
            $"Discarded draft journal entry {entry.EntryNumber}",
            ("JournalEntry", (int)entry.Id));
        await db.SaveChangesAsync(cancellationToken);
    }
}
