using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Maker-checker async approval (§5.7): a distinct controller approves a manual JE that was routed to
/// <c>PendingApproval</c> because it exceeded the book's maker-checker threshold without an up-front
/// approver. Approval finalizes the entry to <c>Posted</c> and folds it into the ledger via
/// <see cref="IPostingEngine.ApprovePendingAsync"/>. DARK behind <c>CAP-ACCT-FULLGL</c> like its create sibling;
/// the engine's <see cref="PostingException"/> for not-found / not-pending / approver-not-distinct surfaces at
/// the controller edge.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record ApproveJournalEntryCommand(long EntryId) : IRequest<ManualJournalEntryResult>;

public class ApproveJournalEntryHandler(
    IPostingEngine postingEngine,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<ApproveJournalEntryCommand, ManualJournalEntryResult>
{
    public async Task<ManualJournalEntryResult> Handle(
        ApproveJournalEntryCommand request, CancellationToken cancellationToken)
    {
        // Server-trusted approver principal — the engine enforces it differs from the submitter (§5.7).
        var approvedByUserId = int.Parse(
            httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        var entry = await postingEngine.ApprovePendingAsync(request.EntryId, approvedByUserId, cancellationToken);
        return entry.ToManualResult();
    }
}
