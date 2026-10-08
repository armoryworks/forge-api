using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Vendors.Addresses;

public record DeleteVendorAddressCommand(int VendorId, int AddressId) : IRequest;

public class DeleteVendorAddressHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteVendorAddressCommand>
{
    public async Task Handle(DeleteVendorAddressCommand request, CancellationToken cancellationToken)
    {
        var address = await db.VendorAddresses
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.VendorId == request.VendorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor address {request.AddressId} not found");

        address.DeletedAt = clock.UtcNow;

        db.LogActivityAt(
            "address-removed",
            $"{VendorAddressRules.Describe(address.AddressType)} address removed: {address.Label}",
            ("Vendor", request.VendorId));

        await db.SaveChangesAsync(cancellationToken);
    }
}
