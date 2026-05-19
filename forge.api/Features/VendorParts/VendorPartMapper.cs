using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Pillar 3 — Internal mapping from <see cref="VendorPart"/> + its loaded
/// navigation properties to the response model. Centralized here so every
/// handler shapes the wire payload identically.
/// </summary>
internal static class VendorPartMapper
{
    public static VendorPartResponseModel ToResponse(VendorPart vp)
    {
        // When the vendor IS the manufacturer we don't store ManufacturerName
        // (the column is null) — readers see the vendor's company name. Keeps
        // a single source of truth and avoids drift on vendor renames.
        var effectiveMfrName = vp.IsManufacturer
            ? (vp.Vendor?.CompanyName ?? vp.ManufacturerName)
            : vp.ManufacturerName;

        return new VendorPartResponseModel(
            Id: vp.Id,
            VendorId: vp.VendorId,
            VendorCompanyName: vp.Vendor?.CompanyName ?? string.Empty,
            PartId: vp.PartId,
            PartNumber: vp.Part?.PartNumber ?? string.Empty,
            PartName: vp.Part?.Name ?? string.Empty,
            VendorPartNumber: vp.VendorPartNumber,
            ManufacturerName: effectiveMfrName,
            VendorMpn: vp.VendorMpn,
            LeadTimeDays: vp.LeadTimeDays,
            MinOrderQty: vp.MinOrderQty,
            PackSize: vp.PackSize,
            CountryOfOrigin: vp.CountryOfOrigin,
            HtsCode: vp.HtsCode,
            IsApproved: vp.IsApproved,
            IsPreferred: vp.IsPreferred,
            Certifications: vp.Certifications,
            LastQuotedDate: vp.LastQuotedDate,
            Notes: vp.Notes,
            PriceTiers: vp.PriceTiers
                .OrderBy(t => t.MinQuantity)
                .ThenByDescending(t => t.EffectiveFrom)
                .Select(ToTierResponse)
                .ToList(),
            CreatedAt: vp.CreatedAt,
            UpdatedAt: vp.UpdatedAt,
            Currency: vp.Currency,
            IsManufacturer: vp.IsManufacturer);
    }

    public static VendorPartPriceTierResponseModel ToTierResponse(VendorPartPriceTier t) =>
        new(
            Id: t.Id,
            VendorPartId: t.VendorPartId,
            MinQuantity: t.MinQuantity,
            UnitPrice: t.UnitPrice,
            Currency: t.Currency,
            EffectiveFrom: t.EffectiveFrom,
            EffectiveTo: t.EffectiveTo,
            Notes: t.Notes);
}
