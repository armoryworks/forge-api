using MediatR;
using QBEngineer.Core.Interfaces;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.CustomerAddresses;

public record DeleteCustomerAddressCommand(int Id) : IRequest;

public class DeleteCustomerAddressHandler(ICustomerAddressRepository repo, AppDbContext db, IClock clock)
    : IRequestHandler<DeleteCustomerAddressCommand>
{
    public async Task Handle(DeleteCustomerAddressCommand request, CancellationToken cancellationToken)
    {
        var address = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Address {request.Id} not found");

        address.DeletedAt = clock.UtcNow;
        // DeletedBy auto-stamped by AppDbContext.SetTimestamps.

        db.LogActivityAt(
            "address-removed",
            $"Removed {address.AddressType} address: {address.Label}",
            ("Customer", address.CustomerId));

        await repo.SaveChangesAsync(cancellationToken);
    }
}
