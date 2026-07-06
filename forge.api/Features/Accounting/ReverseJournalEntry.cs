using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// §5A "Reverse / correct": reverse a Posted journal entry by posting an equal-and-opposite entry and
/// flipping the original to <c>Reversed</c> (with a <c>ReversedByEntryId</c> link), in one transaction.
/// Corrections are reversing entries — the original is never edited (§5.2). Thin exposure of
/// <see cref="IPostingEngine.ReverseAsync"/>, which enforces the preconditions (original is Posted, not
/// already reversed, target period not HardClosed) and the SoD <c>ReverseJournalEntry</c> capability at
/// the engine boundary. DARK behind <c>CAP-ACCT-FULLGL</c>.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record ReverseJournalEntryCommand(long EntryId, DateOnly ReversalDate, string Reason)
    : IRequest<ManualJournalEntryResult>;

/// <summary>Request body for the reverse endpoint (the entry id comes from the route).</summary>
public record ReverseJournalEntryRequest(DateOnly ReversalDate, string Reason);

public class ReverseJournalEntryValidator : AbstractValidator<ReverseJournalEntryCommand>
{
    public ReverseJournalEntryValidator()
    {
        RuleFor(x => x.EntryId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class ReverseJournalEntryHandler(IPostingEngine postingEngine, IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<ReverseJournalEntryCommand, ManualJournalEntryResult>
{
    public async Task<ManualJournalEntryResult> Handle(ReverseJournalEntryCommand request, CancellationToken cancellationToken)
    {
        // Server-trusted principal (§5.2, §5.7) — never client-supplied; the engine records the reverser
        // and enforces the ReverseJournalEntry SoD capability + the reversal preconditions.
        var reversedByUserId = int.Parse(
            httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        var reversal = await postingEngine.ReverseAsync(
            request.EntryId, request.ReversalDate, request.Reason, reversedByUserId, cancellationToken);

        return reversal.ToManualResult();
    }
}
