using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Vendors.Addresses;

public record CreateVendorAddressCommand(
    int VendorId,
    string Label,
    string AddressType,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country,
    bool IsDefault,
    bool IsActive = true) : IRequest<VendorAddressResponseModel>;

public class CreateVendorAddressValidator : AbstractValidator<CreateVendorAddressCommand>
{
    public CreateVendorAddressValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AddressType)
            .Must(t => VendorAddressRules.TryParseType(t, out _))
            .WithMessage("Address type must be one of RemitTo, OrderFrom, ShipFrom, Billing, Other.");
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(50);
        RuleFor(x => x.State)
            .Must((x, state) => VendorAddressRules.IsValidState(x.Country, state))
            .WithMessage("State must be a two-letter code for US addresses.");
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(10);
    }
}

public class CreateVendorAddressHandler(AppDbContext db)
    : IRequestHandler<CreateVendorAddressCommand, VendorAddressResponseModel>
{
    public async Task<VendorAddressResponseModel> Handle(CreateVendorAddressCommand request, CancellationToken cancellationToken)
    {
        await VendorAddressRules.EnsureVendorExistsAsync(db, request.VendorId, cancellationToken);

        if (!VendorAddressRules.TryParseType(request.AddressType, out var addressType))
            throw new ValidationException($"Unknown vendor address type '{request.AddressType}'");

        var address = new VendorAddress
        {
            VendorId = request.VendorId,
            Label = request.Label.Trim(),
            AddressType = addressType,
            Line1 = request.Line1.Trim(),
            Line2 = string.IsNullOrWhiteSpace(request.Line2) ? null : request.Line2.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim(),
            PostalCode = request.PostalCode.Trim(),
            Country = request.Country.Trim(),
            IsDefault = request.IsDefault,
            IsActive = request.IsActive,
        };

        List<VendorAddress> demoted = address.IsDefault
            ? await VendorAddressRules.ClearOtherDefaultsAsync(db, request.VendorId, address.Id, addressType, cancellationToken)
            : [];

        db.VendorAddresses.Add(address);
        db.LogActivityAt(
            "address-added",
            $"{VendorAddressRules.Describe(addressType)} address added: {address.Label} — {address.Line1}, {address.City}, {address.State}{(address.IsDefault ? " (default)" : "")}{VendorAddressRules.DescribeReplaced(demoted)}",
            ("Vendor", request.VendorId));

        await db.SaveChangesAsync(cancellationToken);

        return VendorAddressRules.ToResponseModel(address);
    }
}
