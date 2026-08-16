using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Customers;

public record GetContactInteractionsQuery(int CustomerId, int? ContactId)
    : IRequest<List<ContactInteractionResponseModel>>;

public class GetContactInteractionsHandler(AppDbContext db)
    : IRequestHandler<GetContactInteractionsQuery, List<ContactInteractionResponseModel>>
{
    public async Task<List<ContactInteractionResponseModel>> Handle(
        GetContactInteractionsQuery request, CancellationToken cancellationToken)
    {
        // Get contact IDs for this customer
        var contactIds = await db.Contacts
            .Where(c => c.CustomerId == request.CustomerId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var query = db.Communications
            // Contact-scoped view: the generalization allows party-level rows
            // with no contact, and those belong on the party timeline, not here.
            .Where(ci => ci.ContactId != null && contactIds.Contains(ci.ContactId.Value));

        if (request.ContactId.HasValue)
            query = query.Where(ci => ci.ContactId == request.ContactId.Value);

        return await query
            .OrderByDescending(ci => ci.OccurredAt)
            .Select(ci => new ContactInteractionResponseModel(
                ci.Id,
                ci.ContactId!.Value,
                ci.Contact!.LastName + ", " + ci.Contact.FirstName,
                ci.HandledByUserId ?? 0,
                db.Users
                    .Where(u => u.Id == ci.HandledByUserId)
                    .Select(u => u.LastName + ", " + u.FirstName)
                    .FirstOrDefault() ?? "",
                ci.Type.ToString(),
                ci.Subject,
                ci.Body,
                ci.OccurredAt,
                ci.DurationMinutes,
                ci.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
