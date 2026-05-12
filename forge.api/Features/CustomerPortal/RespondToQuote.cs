using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// Phase 1q — customer's accept/decline action on a Sent quote, performed
/// from the portal. Only Sent quotes are accepted/declined; anything else
/// (Draft / Accepted already / Expired / Converted) returns 409.
///
/// Activity log: emitted on the quote so the staff side sees who responded.
/// </summary>
public record RespondToQuoteCommand(int QuoteId, int CustomerId, int ContactId, bool Accepted) : IRequest;

public class RespondToQuoteHandler(AppDbContext db)
    : IRequestHandler<RespondToQuoteCommand>
{
    public async Task Handle(RespondToQuoteCommand request, CancellationToken ct)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(
            q => q.Id == request.QuoteId && q.CustomerId == request.CustomerId, ct)
            ?? throw new KeyNotFoundException($"Quote {request.QuoteId} not found.");

        if (quote.Status != QuoteStatus.Sent)
        {
            throw new InvalidOperationException(
                $"Quote {quote.Id} is in status {quote.Status} — only Sent quotes can be accepted or declined.");
        }

        quote.Status = request.Accepted ? QuoteStatus.Accepted : QuoteStatus.Declined;
        if (request.Accepted) quote.AcceptedDate = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
