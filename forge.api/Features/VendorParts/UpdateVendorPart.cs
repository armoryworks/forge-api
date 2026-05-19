using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Pillar 3 — Update sourcing metadata on an existing VendorPart. VendorId /
/// PartId are immutable post-create. Sets the at-most-one-preferred-per-Part
/// invariant identically to <see cref="CreateVendorPartHandler"/>.
/// </summary>
public record UpdateVendorPartCommand(int Id, UpdateVendorPartRequestModel Body)
    : IRequest<VendorPartResponseModel>;

public class UpdateVendorPartValidator : AbstractValidator<UpdateVendorPartCommand>
{
    public UpdateVendorPartValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Body.VendorPartNumber).MaximumLength(100);
        RuleFor(x => x.Body.ManufacturerName).MaximumLength(200);
        RuleFor(x => x.Body.VendorMpn).MaximumLength(100);
        RuleFor(x => x.Body.CountryOfOrigin).MaximumLength(2);
        RuleFor(x => x.Body.HtsCode).MaximumLength(20);
        RuleFor(x => x.Body.Notes).MaximumLength(2000);
        RuleFor(x => x.Body.LeadTimeDays).GreaterThanOrEqualTo(0).When(x => x.Body.LeadTimeDays.HasValue);
        RuleFor(x => x.Body.MinOrderQty).GreaterThanOrEqualTo(0).When(x => x.Body.MinOrderQty.HasValue);
        RuleFor(x => x.Body.PackSize).GreaterThanOrEqualTo(0).When(x => x.Body.PackSize.HasValue);
    }
}

public class UpdateVendorPartHandler(AppDbContext db)
    : IRequestHandler<UpdateVendorPartCommand, VendorPartResponseModel>
{
    public async Task<VendorPartResponseModel> Handle(UpdateVendorPartCommand request, CancellationToken ct)
    {
        var vp = await db.VendorParts
            .Include(x => x.Vendor)
            .Include(x => x.PriceTiers)
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"VendorPart {request.Id} not found");

        var body = request.Body;
        var changedFields = new List<string>();

        // Per-field patch — null means "leave alone". Trimming on string
        // fields mirrors CreateVendorPartHandler.
        if (body.VendorPartNumber is not null && body.VendorPartNumber.Trim() != (vp.VendorPartNumber ?? ""))
        {
            vp.VendorPartNumber = body.VendorPartNumber.Trim();
            changedFields.Add("vendorPartNumber");
        }
        if (body.ManufacturerName is not null)
        {
            var trimmed = body.ManufacturerName.Trim();
            var newVal = trimmed.Length == 0 ? null : trimmed;
            if (newVal != vp.ManufacturerName) { vp.ManufacturerName = newVal; changedFields.Add("manufacturerName"); }
        }
        if (body.VendorMpn is not null && body.VendorMpn.Trim() != (vp.VendorMpn ?? ""))
        {
            vp.VendorMpn = body.VendorMpn.Trim();
            changedFields.Add("vendorMpn");
        }

        // Direct-from-MFR flag. Track the flip itself, then normalize the
        // PN/MPN/ManufacturerName fields per the contract below. We apply
        // the normalization AFTER the raw per-field patches above so the
        // flag wins when both are present in the same request (the natural
        // case: user ticks the box, the dialog also sends the values it
        // had visible).
        if (body.IsManufacturer != vp.IsManufacturer)
        {
            vp.IsManufacturer = body.IsManufacturer;
            changedFields.Add("isManufacturer");
        }
        if (vp.IsManufacturer)
        {
            // ManufacturerName is derived from Vendor.CompanyName on read,
            // so the stored column must be null. If a caller sent a value
            // it'd be overwritten silently, but the activity log already
            // recorded the manufacturerName change above (if any) — leave
            // that breadcrumb in place.
            if (vp.ManufacturerName is not null)
            {
                vp.ManufacturerName = null;
                if (!changedFields.Contains("manufacturerName"))
                    changedFields.Add("manufacturerName");
            }

            // Mirror PN ↔ MPN to a single identifier. Prefer VendorPartNumber
            // if set, otherwise VendorMpn. Whichever differed after this
            // collapses to the same value gets a changedFields entry.
            var single = !string.IsNullOrWhiteSpace(vp.VendorPartNumber)
                ? vp.VendorPartNumber
                : vp.VendorMpn;
            if (vp.VendorPartNumber != single)
            {
                vp.VendorPartNumber = single;
                if (!changedFields.Contains("vendorPartNumber"))
                    changedFields.Add("vendorPartNumber");
            }
            if (vp.VendorMpn != single)
            {
                vp.VendorMpn = single;
                if (!changedFields.Contains("vendorMpn"))
                    changedFields.Add("vendorMpn");
            }
        }
        if (body.LeadTimeDays.HasValue && body.LeadTimeDays.Value != vp.LeadTimeDays)
        {
            vp.LeadTimeDays = body.LeadTimeDays.Value;
            changedFields.Add("leadTimeDays");
        }
        if (body.MinOrderQty.HasValue && body.MinOrderQty.Value != vp.MinOrderQty)
        {
            vp.MinOrderQty = body.MinOrderQty.Value;
            changedFields.Add("minOrderQty");
        }
        if (body.PackSize.HasValue && body.PackSize.Value != vp.PackSize)
        {
            vp.PackSize = body.PackSize.Value;
            changedFields.Add("packSize");
        }
        if (body.CountryOfOrigin is not null)
        {
            var newVal = body.CountryOfOrigin.Trim().ToUpperInvariant();
            if (newVal != (vp.CountryOfOrigin ?? "")) { vp.CountryOfOrigin = newVal; changedFields.Add("countryOfOrigin"); }
        }
        if (body.HtsCode is not null && body.HtsCode.Trim() != (vp.HtsCode ?? ""))
        {
            vp.HtsCode = body.HtsCode.Trim();
            changedFields.Add("htsCode");
        }
        if (body.Certifications is not null && body.Certifications != (vp.Certifications ?? ""))
        {
            vp.Certifications = body.Certifications;
            changedFields.Add("certifications");
        }
        if (body.LastQuotedDate.HasValue && body.LastQuotedDate.Value != vp.LastQuotedDate)
        {
            vp.LastQuotedDate = body.LastQuotedDate.Value;
            changedFields.Add("lastQuotedDate");
        }
        if (body.Notes is not null && body.Notes != (vp.Notes ?? ""))
        {
            vp.Notes = body.Notes;
            changedFields.Add("notes");
        }
        if (body.Currency is not null)
        {
            var newCurrency = body.Currency.Trim().ToUpperInvariant();
            if (newCurrency.Length > 0 && newCurrency != vp.Currency)
            {
                vp.Currency = newCurrency;
                changedFields.Add("currency");
            }
        }

        if (body.IsApproved != vp.IsApproved)
        {
            vp.IsApproved = body.IsApproved;
            changedFields.Add("isApproved");
        }

        // Preferred-flip guard — only when transitioning false → true do we
        // need to clear siblings; otherwise leave the rest of the AVL alone.
        var preferredFlipped = body.IsPreferred != vp.IsPreferred;
        if (body.IsPreferred && !vp.IsPreferred)
        {
            var siblings = await db.VendorParts
                .Where(other => other.PartId == vp.PartId
                    && other.Id != vp.Id
                    && other.IsPreferred)
                .ToListAsync(ct);
            foreach (var sib in siblings)
                sib.IsPreferred = false;
        }
        vp.IsPreferred = body.IsPreferred;
        if (preferredFlipped) changedFields.Add("isPreferred");

        // Indexing-points rule: a VendorPart row sits between Part and
        // Vendor — log on both. Rollup rule: one row per request, summarizing
        // all changes (don't emit per-field rows here).
        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "vendor-source-updated",
                $"Updated vendor source {vp.Vendor?.CompanyName ?? "(unknown)"}: {string.Join(", ", changedFields)}",
                ("Part", vp.PartId),
                ("Vendor", vp.VendorId));
        }

        await db.SaveChangesAsync(ct);

        var loaded = await db.VendorParts
            .Include(x => x.Vendor)
            .Include(x => x.Part)
            .Include(x => x.PriceTiers)
            .AsNoTracking()
            .FirstAsync(x => x.Id == vp.Id, ct);

        return VendorPartMapper.ToResponse(loaded);
    }
}
