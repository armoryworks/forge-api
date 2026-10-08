using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Vendors.Addresses;

public record GetVendorAddressesQuery(int VendorId, bool IncludeInactive = false)
    : IRequest<List<VendorAddressResponseModel>>;

public class GetVendorAddressesHandler(AppDbContext db)
    : IRequestHandler<GetVendorAddressesQuery, List<VendorAddressResponseModel>>
{
    public async Task<List<VendorAddressResponseModel>> Handle(GetVendorAddressesQuery request, CancellationToken cancellationToken)
    {
        await VendorAddressRules.EnsureVendorExistsAsync(db, request.VendorId, cancellationToken);

        var addresses = await db.VendorAddresses
            .AsNoTracking()
            .Where(a => a.VendorId == request.VendorId && (request.IncludeInactive || a.IsActive))
            .OrderBy(a => a.AddressType)
            .ThenByDescending(a => a.IsDefault)
            .ThenBy(a => a.Label)
            .ToListAsync(cancellationToken);

        return addresses.Select(VendorAddressRules.ToResponseModel).ToList();
    }
}
