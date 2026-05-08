using FluentValidation;
using MediatR;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.CustomerAddresses;

public record CreateCustomerAddressCommand(
    int CustomerId,
    string Label,
    string AddressType,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country,
    bool IsDefault) : IRequest<CustomerAddressResponseModel>;

public class CreateCustomerAddressValidator : AbstractValidator<CreateCustomerAddressCommand>
{
    public CreateCustomerAddressValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(10);
    }
}

public class CreateCustomerAddressHandler(
    ICustomerAddressRepository repo,
    ICustomerRepository customerRepo,
    AppDbContext db)
    : IRequestHandler<CreateCustomerAddressCommand, CustomerAddressResponseModel>
{
    public async Task<CustomerAddressResponseModel> Handle(CreateCustomerAddressCommand request, CancellationToken cancellationToken)
    {
        _ = await customerRepo.FindAsync(request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var addressType = Enum.Parse<AddressType>(request.AddressType, true);

        var address = new CustomerAddress
        {
            CustomerId = request.CustomerId,
            Label = request.Label,
            AddressType = addressType,
            Line1 = request.Line1,
            Line2 = request.Line2,
            City = request.City,
            State = request.State,
            PostalCode = request.PostalCode,
            Country = request.Country,
            IsDefault = request.IsDefault,
        };

        await repo.AddAsync(address, cancellationToken);

        // Address has semantics (default flag, billing/shipping type) so the
        // collection doesn't qualify for the self-auditing-data exception —
        // log on the customer.
        db.LogActivityAt(
            "address-added",
            $"Added {addressType} address: {address.Label} — {address.Line1}, {address.City}, {address.State}{(address.IsDefault ? " (default)" : "")}",
            ("Customer", request.CustomerId));

        await repo.SaveChangesAsync(cancellationToken);

        return new CustomerAddressResponseModel(
            address.Id, address.Label, address.AddressType.ToString(),
            address.Line1, address.Line2, address.City, address.State,
            address.PostalCode, address.Country, address.IsDefault);
    }
}
