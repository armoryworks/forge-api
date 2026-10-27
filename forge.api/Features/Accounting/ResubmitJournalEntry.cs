using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Maker-checker edit-and-resubmit (§5.7): the submitter of a rejected manual JE (returned to <c>Draft</c>)
/// corrects its date, memo and lines and sends it back to <c>PendingApproval</c> through
/// <see cref="IPostingEngine.ResubmitDraftAsync"/>. The entry keeps its <c>EntryNumber</c>, so the journal
/// sequence has no gap. DARK behind <c>CAP-ACCT-FULLGL</c> like its create sibling.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record ResubmitJournalEntryCommand(long EntryId, CreateManualJournalEntryCommand Entry)
    : IRequest<ManualJournalEntryResult>;

public class ResubmitJournalEntryValidator : AbstractValidator<ResubmitJournalEntryCommand>
{
    public ResubmitJournalEntryValidator()
    {
        RuleFor(x => x.EntryId).GreaterThan(0);
        RuleFor(x => x.Entry).NotNull().SetValidator(new CreateManualJournalEntryValidator());
    }
}

public class ResubmitJournalEntryHandler(
    IPostingEngine postingEngine,
    IHttpContextAccessor httpContextAccessor,
    AppDbContext? db = null)
    : IRequestHandler<ResubmitJournalEntryCommand, ManualJournalEntryResult>
{
    public async Task<ManualJournalEntryResult> Handle(
        ResubmitJournalEntryCommand request, CancellationToken cancellationToken)
    {
        var submittedByUserId = int.Parse(
            httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        var entry = await postingEngine.ResubmitDraftAsync(
            request.EntryId,
            CreateManualJournalEntryHandler.ToPostingRequest(request.Entry),
            submittedByUserId,
            cancellationToken);
        if (db is not null)
            await CreateManualJournalEntryHandler.LogSoftCloseOverrideAsync(db, request.Entry, entry, cancellationToken);
        return entry.ToManualResult();
    }
}
