using FluentValidation;
using MediatR;
using Forge.Api.Workflows;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

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
    bool? Is1099 = null,
    string? TaxId = null,
    // User-settable vendor number — see UpdateVendorRequestModel.VendorNumber.
    string? VendorNumber = null,
    string? Fax = null) : IRequest;

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
        RuleFor(x => x.TaxId).MaximumLength(32).When(x => x.TaxId != null);
        RuleFor(x => x.Fax).MaximumLength(50).When(x => x.Fax != null);
    }
}

public class UpdateVendorHandler(
    IVendorRepository repo,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    AppDbContext db,
    IClock clock)
    : IRequestHandler<UpdateVendorCommand>
{
    // System setting that gates caller-supplied vendor numbers (shared with CreateVendor).
    private const string AllowManualVendorNumbersKey = "vendors.allow_manual_numbers";

    public async Task Handle(UpdateVendorCommand request, CancellationToken cancellationToken)
    {
        var vendor = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor {request.Id} not found");

        var changedFields = new List<string>();

        // User-settable vendor number — only when manual numbers are enabled, and only after a
        // uniqueness check that excludes this vendor. The DB partial-unique index is the final
        // backstop.
        if (request.VendorNumber is not null)
        {
            var newNumber = request.VendorNumber.Trim();
            if (newNumber.Length > 0 && !string.Equals(newNumber, vendor.VendorNumber, StringComparison.Ordinal))
            {
                if (!await ManualNumbersAllowedAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "Vendor numbers are typed automatically. An admin can allow manual numbers in Admin > Settings > Numbering.");
                if (await repo.VendorNumberExistsAsync(newNumber, vendor.Id, cancellationToken))
                    throw new InvalidOperationException($"Vendor number '{newNumber}' is already in use.");
                // Ensure the current number is on record (covers legacy vendors with none), then
                // supersede it — the old number stays resolvable. RenameAsync alone opens the
                // first active row when the current value is null.
                if (!string.IsNullOrWhiteSpace(vendor.VendorNumber))
                    await identifiers.IssueAsync(BusinessEntityType.Vendor, vendor.Id, vendor.VendorNumber, cancellationToken);
                await identifiers.RenameAsync(BusinessEntityType.Vendor, vendor.Id, newNumber, cancellationToken);
                vendor.VendorNumber = newNumber;
                changedFields.Add("vendorNumber");
            }
        }

        if (request.CompanyName != null && request.CompanyName != vendor.CompanyName)
        {
            vendor.CompanyName = request.CompanyName;
            changedFields.Add("companyName");
        }
        if (request.ContactName != null && request.ContactName != vendor.ContactName)
        {
            vendor.ContactName = request.ContactName;
            changedFields.Add("contactName");
        }
        if (request.Email != null && request.Email.NullIfEmpty() != vendor.Email)
        {
            vendor.Email = request.Email.NullIfEmpty();
            changedFields.Add("email");
        }
        if (request.Phone != null && request.Phone.NullIfEmpty() != vendor.Phone)
        {
            vendor.Phone = request.Phone.NullIfEmpty();
            changedFields.Add("phone");
        }
        if (request.Fax != null && request.Fax.NullIfEmpty() != vendor.Fax)
        {
            vendor.Fax = request.Fax.NullIfEmpty();
            changedFields.Add("fax");
        }
        if (request.Address != null && request.Address != vendor.Address)
        {
            vendor.Address = request.Address;
            changedFields.Add("address");
        }
        if (request.City != null && request.City != vendor.City)
        {
            vendor.City = request.City;
            changedFields.Add("city");
        }
        if (request.State != null && request.State != vendor.State)
        {
            vendor.State = request.State;
            changedFields.Add("state");
        }
        if (request.ZipCode != null && request.ZipCode != vendor.ZipCode)
        {
            vendor.ZipCode = request.ZipCode;
            changedFields.Add("zipCode");
        }
        if (request.Country != null && request.Country != vendor.Country)
        {
            vendor.Country = request.Country;
            changedFields.Add("country");
        }
        if (request.PaymentTerms != null && request.PaymentTerms != vendor.PaymentTerms)
        {
            vendor.PaymentTerms = request.PaymentTerms;
            changedFields.Add("paymentTerms");
        }
        if (request.Notes != null && request.Notes != vendor.Notes)
        {
            vendor.Notes = request.Notes;
            changedFields.Add("notes");
        }
        // V9: off-tier variance % round-trips (was silently dropped — request model omitted it).
        if (request.OffTierVariancePct.HasValue && request.OffTierVariancePct != vendor.OffTierVariancePct)
        {
            vendor.OffTierVariancePct = request.OffTierVariancePct;
            changedFields.Add("offTierVariancePct");
        }
        if (request.Is1099.HasValue && request.Is1099.Value != vendor.Is1099)
        {
            vendor.Is1099 = request.Is1099.Value;
            changedFields.Add("is1099");
        }
        if (request.TaxId != null)
        {
            var taxId = string.IsNullOrWhiteSpace(request.TaxId) ? null : request.TaxId.Trim();
            if (taxId != vendor.TaxId)
            {
                vendor.TaxId = taxId;
                changedFields.Add("taxId");
            }
        }

        // Phase 3 H2 / WU-12: stamp DeactivationDate when transitioning
        // active → inactive; clear it on reactivation. Drives the lifecycle
        // grace window (existing in-flight POs continue; new POs blocked).
        if (request.IsActive.HasValue && request.IsActive.Value != vendor.IsActive)
        {
            vendor.IsActive = request.IsActive.Value;
            vendor.DeactivationDate = vendor.IsActive ? null : clock.UtcNow;
            changedFields.Add(vendor.IsActive ? "reactivated" : "deactivated");
        }

        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "updated",
                $"Updated {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}",
                ("Vendor", vendor.Id));
        }

        await repo.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ManualNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualVendorNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
