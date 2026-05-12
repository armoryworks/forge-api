using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Customers;

public record DeleteContactCommand(int CustomerId, int ContactId) : IRequest;

public class DeleteContactHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteContactCommand>
{
    public async Task Handle(DeleteContactCommand request, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts
            .FirstOrDefaultAsync(c => c.Id == request.ContactId && c.CustomerId == request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Contact {request.ContactId} not found");

        contact.DeletedAt = clock.UtcNow;
        // DeletedBy is auto-stamped by AppDbContext.SetTimestamps.

        db.LogActivityAt(
            "contact-removed",
            $"Removed contact: {contact.LastName}, {contact.FirstName}",
            ("Customer", request.CustomerId),
            ("Contact", contact.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
