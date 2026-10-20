using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// Phase 1q — customer's accept/decline action on a Sent quote, performed
/// from the portal. Only Sent quotes are accepted/declined; anything else
/// (Draft / Accepted already / Expired / Converted) returns 409. A Sent quote
/// past its expiration date (see <see cref="PortalReadRules"/>) can still be
/// declined but no longer accepted.
///
/// Activity log: besides the automatic Status field-change row, a
/// portal_accepted / portal_declined row on the quote names the responding
/// contact so the staff side sees who responded.
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
        if (request.Accepted && PortalReadRules.IsExpired(quote.ExpirationDate, validFrom))
        {
            throw new InvalidOperationException(
                $"Quote {quoteLabel} expired on {quote.ExpirationDate!.Value.UtcDateTime:yyyy-MM-dd} and can no longer be accepted.");
        }

        quote.Status = request.Accepted ? QuoteStatus.Accepted : QuoteStatus.Declined;
        if (request.Accepted)
        {
            quote.AcceptedDate = clock.UtcNow;
            // Stamp the accepting customer contact — this is what marks the acceptance as genuine
            // customer proof (vs a staff accept), so a converted order can auto-satisfy its gate.
            quote.AcceptedByContactId = request.ContactId;
        }

        var contactName = await db.Contacts.AsNoTracking()
            .Where(c => c.Id == request.ContactId)
            .Select(c => (c.FirstName + " " + c.LastName).Trim())
            .FirstOrDefaultAsync(ct);
        var action = request.Accepted ? "accepted" : "declined";
        db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "Quote",
            EntityId = quote.Id,
            Action = request.Accepted ? "portal_accepted" : "portal_declined",
            Description = $"Quote {quoteLabel} {action} in the customer portal by {(string.IsNullOrEmpty(contactName) ? $"contact #{request.ContactId}" : contactName)}.",
            CreatedAt = clock.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }
}
