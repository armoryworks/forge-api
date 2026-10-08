using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Vendors.Contacts;

public record GetVendorContactsQuery(int VendorId, bool IncludeInactive = false)
    : IRequest<List<VendorContactResponseModel>>;

public class GetVendorContactsHandler(AppDbContext db)
    : IRequestHandler<GetVendorContactsQuery, List<VendorContactResponseModel>>
{
    public async Task<List<VendorContactResponseModel>> Handle(GetVendorContactsQuery request, CancellationToken cancellationToken)
    {
        await VendorContactRules.EnsureVendorExistsAsync(db, request.VendorId, cancellationToken);

        var contacts = await db.VendorContacts
            .AsNoTracking()
            .Where(c => c.VendorId == request.VendorId && (request.IncludeInactive || c.IsActive))
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .ToListAsync(cancellationToken);

        return contacts.Select(VendorContactRules.ToResponseModel).ToList();
    }
}
