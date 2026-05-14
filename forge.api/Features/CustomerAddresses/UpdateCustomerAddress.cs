using FluentValidation;
using MediatR;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CustomerAddresses;

public record UpdateCustomerAddressCommand(
    int Id,
    string Label,
    string AddressType,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country,
    bool IsDefault) : IRequest;

public class UpdateCustomerAddressValidator : AbstractValidator<UpdateCustomerAddressCommand>
{
    public UpdateCustomerAddressValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AddressType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200).When(x => x.Line2 is not null);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
    }
}

public class UpdateCustomerAddressHandler(ICustomerAddressRepository repo, AppDbContext db)
    : IRequestHandler<UpdateCustomerAddressCommand>
{
    public async Task Handle(UpdateCustomerAddressCommand request, CancellationToken cancellationToken)
    {
        var address = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Address {request.Id} not found");

        var changedFields = new List<string>();
        var newType = Enum.Parse<AddressType>(request.AddressType, true);

        if (request.Label != address.Label) { address.Label = request.Label; changedFields.Add("label"); }
        if (newType != address.AddressType) { address.AddressType = newType; changedFields.Add("addressType"); }
        if (request.Line1 != address.Line1) { address.Line1 = request.Line1; changedFields.Add("line1"); }
        if (request.Line2 != address.Line2) { address.Line2 = request.Line2; changedFields.Add("line2"); }
        if (request.City != address.City) { address.City = request.City; changedFields.Add("city"); }
        if (request.State != address.State) { address.State = request.State; changedFields.Add("state"); }
        if (request.PostalCode != address.PostalCode) { address.PostalCode = request.PostalCode; changedFields.Add("postalCode"); }
        if (request.Country != address.Country) { address.Country = request.Country; changedFields.Add("country"); }
        if (request.IsDefault != address.IsDefault)
        {
            address.IsDefault = request.IsDefault;
            changedFields.Add(address.IsDefault ? "set-default" : "cleared-default");
        }

        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "address-updated",
                $"Updated address ({address.Label}) — {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}",
                ("Customer", address.CustomerId));
        }

        await repo.SaveChangesAsync(cancellationToken);
    }
}
