using FluentValidation;
using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Vendors;

public record UpdateVendorCommand(
    int Id,
    string? CompanyName,
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
    decimal? OffTierVariancePct,
    bool? IsActive,
    // User-settable vendor number — see UpdateVendorRequestModel.VendorNumber.
    string? VendorNumber = null) : IRequest;

public class UpdateVendorValidator : AbstractValidator<UpdateVendorCommand>
{
    public UpdateVendorValidator()
    {
        RuleFor(x => x.CompanyName).MaximumLength(200).When(x => x.CompanyName != null);
        RuleFor(x => x.VendorNumber).NotEmpty().MaximumLength(50).When(x => x.VendorNumber != null);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes != null);
        RuleFor(x => x.OffTierVariancePct)
            .InclusiveBetween(0m, 100m)
            .When(x => x.OffTierVariancePct.HasValue)
            .WithMessage("Off-tier variance % must be between 0 and 100.");
    }
}

public class UpdateVendorHandler(
    IVendorRepository repo,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    IClock clock)
    : IRequestHandler<UpdateVendorCommand>
{
    // System setting that gates caller-supplied vendor numbers (shared with CreateVendor).
    private const string AllowManualVendorNumbersKey = "vendors.allow_manual_numbers";

    public async Task Handle(UpdateVendorCommand request, CancellationToken cancellationToken)
    {
        var vendor = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor {request.Id} not found");

        // User-settable vendor number — only when manual numbers are enabled, and only after a
        // uniqueness check that excludes this vendor. The DB partial-unique index is the final
        // backstop. (UpdateVendorHandler carries no activity log today, so the change is recorded
        // in the identifier registry only.)
        if (request.VendorNumber is not null)
        {
            var newNumber = request.VendorNumber.Trim();
            if (newNumber.Length > 0 && !string.Equals(newNumber, vendor.VendorNumber, StringComparison.Ordinal))
            {
                if (!await ManualNumbersAllowedAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "Manual vendor numbers are disabled. Turn on 'vendors.allow_manual_numbers' in settings to change a vendor number.");
                if (await repo.VendorNumberExistsAsync(newNumber, vendor.Id, cancellationToken))
                    throw new InvalidOperationException($"Vendor number '{newNumber}' is already in use.");
                // Ensure the current number is on record (covers legacy vendors with none), then
                // supersede it — the old number stays resolvable. RenameAsync alone opens the
                // first active row when the current value is null.
                if (!string.IsNullOrWhiteSpace(vendor.VendorNumber))
                    await identifiers.IssueAsync(BusinessEntityType.Vendor, vendor.Id, vendor.VendorNumber, cancellationToken);
                await identifiers.RenameAsync(BusinessEntityType.Vendor, vendor.Id, newNumber, cancellationToken);
                vendor.VendorNumber = newNumber;
            }
        }

        if (request.CompanyName != null) vendor.CompanyName = request.CompanyName;
        if (request.ContactName != null) vendor.ContactName = request.ContactName;
        if (request.Email != null) vendor.Email = request.Email;
        if (request.Phone != null) vendor.Phone = request.Phone;
        if (request.Address != null) vendor.Address = request.Address;
        if (request.City != null) vendor.City = request.City;
        if (request.State != null) vendor.State = request.State;
        if (request.ZipCode != null) vendor.ZipCode = request.ZipCode;
        if (request.Country != null) vendor.Country = request.Country;
        if (request.PaymentTerms != null) vendor.PaymentTerms = request.PaymentTerms;
        if (request.Notes != null) vendor.Notes = request.Notes;
        // V9: off-tier variance % round-trips (was silently dropped — request model omitted it).
        if (request.OffTierVariancePct.HasValue) vendor.OffTierVariancePct = request.OffTierVariancePct;

        // Phase 3 H2 / WU-12: stamp DeactivationDate when transitioning
        // active → inactive; clear it on reactivation. Drives the lifecycle
        // grace window (existing in-flight POs continue; new POs blocked).
        if (request.IsActive.HasValue && request.IsActive.Value != vendor.IsActive)
        {
            vendor.IsActive = request.IsActive.Value;
            vendor.DeactivationDate = vendor.IsActive ? null : clock.UtcNow;
        }

        await repo.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ManualNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualVendorNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
