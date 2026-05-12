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

        var query = db.ContactInteractions
            .Where(ci => contactIds.Contains(ci.ContactId));

        if (request.ContactId.HasValue)
            query = query.Where(ci => ci.ContactId == request.ContactId.Value);

        return await query
            .OrderByDescending(ci => ci.InteractionDate)
            .Select(ci => new ContactInteractionResponseModel(
                ci.Id,
                ci.ContactId,
                ci.Contact.LastName + ", " + ci.Contact.FirstName,
                ci.UserId,
                db.Users
                    .Where(u => u.Id == ci.UserId)
                    .Select(u => u.LastName + ", " + u.FirstName)
                    .FirstOrDefault() ?? "",
                ci.Type.ToString(),
                ci.Subject,
                ci.Body,
                ci.InteractionDate,
                ci.DurationMinutes,
                ci.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
