using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Vendors;

public record CreateVendorCommand(
    string CompanyName,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    string? State,
    string? ZipCode,
    string? Country,
    string? PaymentTerms,
    string? Notes,
    // Optional caller-supplied vendor number — see CreateVendorRequestModel.VendorNumber.
    string? VendorNumber = null) : IRequest<VendorListItemModel>;

public class CreateVendorValidator : AbstractValidator<CreateVendorCommand>
{
    public CreateVendorValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        // Matches the vendors.vendor_number column (varchar(50)). Uniqueness is
        // checked in the handler since it needs a DB lookup.
        RuleFor(x => x.VendorNumber).MaximumLength(50).When(x => !string.IsNullOrWhiteSpace(x.VendorNumber));
        RuleFor(x => x.ContactName).MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateVendorHandler(
    IVendorRepository repo,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers)
    : IRequestHandler<CreateVendorCommand, VendorListItemModel>
{
    // System setting that gates caller-supplied vendor numbers. Stored as "true"/"false".
    private const string AllowManualVendorNumbersKey = "vendors.allow_manual_numbers";

    public async Task<VendorListItemModel> Handle(CreateVendorCommand request, CancellationToken cancellationToken)
    {
        var vendorNumber = await ResolveVendorNumberAsync(request, cancellationToken);

        var vendor = new Vendor
        {
            CompanyName = request.CompanyName,
            VendorNumber = vendorNumber,
            ContactName = request.ContactName,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            City = request.City,
            State = request.State,
            ZipCode = request.ZipCode,
            Country = request.Country,
            PaymentTerms = request.PaymentTerms,
            Notes = request.Notes,
        };

        await repo.AddAsync(vendor, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        // Record the number in the identifier registry (history + resolution).
        await identifiers.IssueAsync(BusinessEntityType.Vendor, vendor.Id, vendor.VendorNumber!, cancellationToken);

        return new VendorListItemModel(
            vendor.Id, vendor.CompanyName, vendor.VendorNumber, vendor.ContactName,
            vendor.Email, vendor.Phone, vendor.IsActive, 0, vendor.CreatedAt);
    }

    // Uses a caller-supplied vendor number when manual numbers are enabled and one
    // was provided; otherwise auto-generates the next sequential number.
    private async Task<string> ResolveVendorNumberAsync(CreateVendorCommand request, CancellationToken ct)
    {
        var supplied = request.VendorNumber?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied) && await ManualNumbersAllowedAsync(ct))
        {
            if (await repo.VendorNumberExistsAsync(supplied, null, ct))
                throw new InvalidOperationException($"Vendor number '{supplied}' is already in use.");
            return supplied;
        }

        return await repo.GenerateNextVendorNumberAsync(ct);
    }

    private async Task<bool> ManualNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualVendorNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
