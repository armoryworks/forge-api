using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Vendors.Contacts;

public record DeleteVendorContactCommand(int VendorId, int ContactId) : IRequest;

public class DeleteVendorContactHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteVendorContactCommand>
{
    public async Task Handle(DeleteVendorContactCommand request, CancellationToken cancellationToken)
    {
        var contact = await db.VendorContacts
            .FirstOrDefaultAsync(c => c.Id == request.ContactId && c.VendorId == request.VendorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor contact {request.ContactId} not found");

        contact.DeletedAt = clock.UtcNow;

        db.LogActivityAt(
            "contact-removed",
            $"Contact removed: {VendorContactRules.DisplayName(contact)}",
            ("Vendor", request.VendorId));

        await db.SaveChangesAsync(cancellationToken);
    }
}
