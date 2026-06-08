using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Maker-checker async rejection (§5.7): a controller declines a <c>PendingApproval</c> manual JE, returning it
/// to <c>Draft</c> (the immutability interceptor leaves pre-Posted entries mutable). The entry was never folded
/// into the ledger-balance read-model, so nothing needs unwinding; its allocated EntryNumber simply becomes a
/// gap (gaps are allowed). The optional reason is appended to the memo for audit. DARK behind <c>CAP-ACCT-FULLGL</c>.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record RejectJournalEntryCommand(long EntryId, string? Reason) : IRequest<ManualJournalEntryResult>;

public class RejectJournalEntryHandler(AppDbContext db)
    : IRequestHandler<RejectJournalEntryCommand, ManualJournalEntryResult>
{
    public async Task<ManualJournalEntryResult> Handle(
        RejectJournalEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == request.EntryId, cancellationToken)
            ?? throw new KeyNotFoundException($"Journal entry {request.EntryId} not found.");

        if (entry.Status != JournalEntryStatus.PendingApproval)
            throw new InvalidOperationException(
                $"Only a PendingApproval entry can be rejected; entry {request.EntryId} is {entry.Status}.");

        entry.Status = JournalEntryStatus.Draft;
        if (!string.IsNullOrWhiteSpace(request.Reason))
            entry.Memo = string.IsNullOrWhiteSpace(entry.Memo)
                ? $"[Rejected] {request.Reason}"
                : $"{entry.Memo} — [Rejected] {request.Reason}";

        await db.SaveChangesAsync(cancellationToken);
        return entry.ToManualResult();
    }
}
