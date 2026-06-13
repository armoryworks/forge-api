using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Lists the manual journal entries awaiting a second approver (<c>PendingApproval</c>) for a book, newest
/// first, with their lines — the approver's work-list for the maker-checker async flow (§5.7). Read-only,
/// DARK behind <c>CAP-ACCT-FULLGL</c>. <c>PostedBy</c> on each result identifies the submitter (the maker), so
/// the approver can see they are not approving their own entry.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetPendingJournalEntriesQuery(int BookId) : IRequest<IReadOnlyList<ManualJournalEntryResult>>;

public class GetPendingJournalEntriesHandler(AppDbContext db)
    : IRequestHandler<GetPendingJournalEntriesQuery, IReadOnlyList<ManualJournalEntryResult>>
{
    public async Task<IReadOnlyList<ManualJournalEntryResult>> Handle(
        GetPendingJournalEntriesQuery request, CancellationToken cancellationToken)
    {
        var entries = await db.JournalEntries
            .AsNoTracking()
            .Include(e => e.Lines)
            .Where(e => e.BookId == request.BookId && e.Status == JournalEntryStatus.PendingApproval)
            .OrderByDescending(e => e.EntryNumber)
            .ToListAsync(cancellationToken);

        return entries.Select(e => e.ToManualResult()).ToList();
    }
}
