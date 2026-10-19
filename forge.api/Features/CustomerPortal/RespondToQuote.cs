using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// Phase 1q — customer's accept/decline action on a Sent quote, performed
/// from the portal. Only Sent quotes that have not passed their expiration
/// date (see <see cref="PortalReadRules"/>) are accepted/declined; anything
/// else (Draft / Accepted already / Expired / Converted) returns 409.
///
/// Activity log: emitted on the quote so the staff side sees who responded.
/// </summary>
public record RespondToQuoteCommand(int QuoteId, int CustomerId, int ContactId, bool Accepted) : IRequest;

public class RespondToQuoteHandler(AppDbContext db, IClock clock)
    : IRequestHandler<RespondToQuoteCommand>
{
    public async Task Handle(RespondToQuoteCommand request, CancellationToken ct)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(
            q => q.Id == request.QuoteId && q.CustomerId == request.CustomerId, ct)
            ?? throw new KeyNotFoundException($"Quote {request.QuoteId} not found.");

        var quoteLabel = quote.QuoteNumber ?? $"#{quote.Id}";
        if (quote.Status != QuoteStatus.Sent)
        {
            throw new InvalidOperationException(
                $"Quote {quoteLabel} is in status {quote.Status} — only Sent quotes can be accepted or declined.");
        }

        var validFrom = await PortalReadRules.QuoteValidFromAsync(db, clock, ct);
        if (PortalReadRules.IsExpired(quote.ExpirationDate, validFrom))
        {
            throw new InvalidOperationException(
                $"Quote {quoteLabel} expired on {quote.ExpirationDate!.Value.UtcDateTime:yyyy-MM-dd} and can no longer be accepted or declined.");
        }

        quote.Status = request.Accepted ? QuoteStatus.Accepted : QuoteStatus.Declined;
        if (request.Accepted)
        {
            quote.AcceptedDate = clock.UtcNow;
            // Stamp the accepting customer contact — this is what marks the acceptance as genuine
            // customer proof (vs a staff accept), so a converted order can auto-satisfy its gate.
            quote.AcceptedByContactId = request.ContactId;
        }

        await db.SaveChangesAsync(ct);
    }
}
