using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Vendors.Addresses;

public record UpdateVendorAddressCommand(
    int VendorId,
    int AddressId,
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

public class UpdateVendorAddressValidator : AbstractValidator<UpdateVendorAddressCommand>
{
    public UpdateVendorAddressValidator()
    {
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

public class UpdateVendorAddressHandler(AppDbContext db)
    : IRequestHandler<UpdateVendorAddressCommand, VendorAddressResponseModel>
{
    public async Task<VendorAddressResponseModel> Handle(UpdateVendorAddressCommand request, CancellationToken cancellationToken)
    {
        var address = await db.VendorAddresses
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.VendorId == request.VendorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor address {request.AddressId} not found");

        if (!VendorAddressRules.TryParseType(request.AddressType, out var newType))
            throw new ValidationException($"Unknown vendor address type '{request.AddressType}'");

        var line2 = string.IsNullOrWhiteSpace(request.Line2) ? null : request.Line2.Trim();
        var changedFields = new List<string>();

        if (request.Label.Trim() != address.Label) { address.Label = request.Label.Trim(); changedFields.Add("label"); }
        if (newType != address.AddressType) { address.AddressType = newType; changedFields.Add("addressType"); }
        if (request.Line1.Trim() != address.Line1) { address.Line1 = request.Line1.Trim(); changedFields.Add("line1"); }
        if (line2 != address.Line2) { address.Line2 = line2; changedFields.Add("line2"); }
        if (request.City.Trim() != address.City) { address.City = request.City.Trim(); changedFields.Add("city"); }
        if (request.State.Trim() != address.State) { address.State = request.State.Trim(); changedFields.Add("state"); }
        if (request.PostalCode.Trim() != address.PostalCode) { address.PostalCode = request.PostalCode.Trim(); changedFields.Add("postalCode"); }
        if (request.Country.Trim() != address.Country) { address.Country = request.Country.Trim(); changedFields.Add("country"); }
        if (request.IsActive != address.IsActive)
        {
            address.IsActive = request.IsActive;
            changedFields.Add(address.IsActive ? "activated" : "deactivated");
        }
        if (request.IsDefault != address.IsDefault)
        {
            address.IsDefault = request.IsDefault;
            changedFields.Add(address.IsDefault ? "set-default" : "cleared-default");
        }

        List<VendorAddress> demoted = address.IsDefault && (changedFields.Contains("set-default") || changedFields.Contains("addressType"))
            ? await VendorAddressRules.ClearOtherDefaultsAsync(db, address.VendorId, address.Id, address.AddressType, cancellationToken)
            : [];

        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "address-updated",
                $"{VendorAddressRules.Describe(address.AddressType)} address updated: {address.Label} — {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}{VendorAddressRules.DescribeReplaced(demoted)}",
                ("Vendor", address.VendorId));
            await db.SaveChangesAsync(cancellationToken);
        }

        return VendorAddressRules.ToResponseModel(address);
    }
}
