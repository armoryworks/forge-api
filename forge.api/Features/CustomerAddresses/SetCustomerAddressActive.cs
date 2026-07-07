using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CustomerAddresses;

/// <summary>
/// Admin-only activate/deactivate. Inactive addresses stay on file for the
/// history of orders that shipped to them; soft-delete remains true removal.
/// Kept as its own endpoint (rather than a field on UpdateCustomerAddress)
/// because the controller-wide roles include non-admins.
/// </summary>
public record SetCustomerAddressActiveCommand(int Id, bool IsActive) : IRequest;

public class SetCustomerAddressActiveValidator : AbstractValidator<SetCustomerAddressActiveCommand>
{
    public SetCustomerAddressActiveValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public class SetCustomerAddressActiveHandler(ICustomerAddressRepository repo, AppDbContext db)
    : IRequestHandler<SetCustomerAddressActiveCommand>
{
    public async Task Handle(SetCustomerAddressActiveCommand request, CancellationToken cancellationToken)
    {
        var address = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Address {request.Id} not found");

        if (address.IsActive == request.IsActive)
            return;

        // The default address is what new orders fall back to — deactivating it
        // would leave the customer with no usable default. Reassign first.
        if (!request.IsActive && address.IsDefault)
            throw new InvalidOperationException(
                "Cannot deactivate the default address. Set another address as default first.");

        address.IsActive = request.IsActive;

        db.LogActivityAt(
            request.IsActive ? "address-reactivated" : "address-deactivated",
            $"{(request.IsActive ? "Reactivated" : "Deactivated")} address ({address.Label})",
            ("Customer", address.CustomerId));

        await repo.SaveChangesAsync(cancellationToken);
    }
}
