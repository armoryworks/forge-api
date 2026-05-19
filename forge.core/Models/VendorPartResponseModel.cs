namespace Forge.Core.Models;

/// <summary>
/// Pillar 3 — Read model for a VendorPart row, denormalized with the vendor
/// company name + part number/name for list-view convenience and including
/// the full PriceTiers collection inline.
/// </summary>
public record VendorPartResponseModel(
    int Id,
    int VendorId,
    string VendorCompanyName,
    int PartId,
    string PartNumber,
    string PartName,
    string? VendorPartNumber,
    /// <summary>
    /// Effective OEM brand. When <see cref="IsManufacturer"/> is true this
    /// is the vendor's own company name (the stored ManufacturerName column
    /// is null in that case to avoid drift). Otherwise it's whatever was
    /// entered for this distributor's OEM claim.
    /// </summary>
    string? ManufacturerName,
    string? VendorMpn,
    int? LeadTimeDays,
    decimal? MinOrderQty,
    decimal? PackSize,
    string? CountryOfOrigin,
    string? HtsCode,
    bool IsApproved,
    bool IsPreferred,
    string? Certifications,
    DateTimeOffset? LastQuotedDate,
    string? Notes,
    List<VendorPartPriceTierResponseModel> PriceTiers,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>ISO-4217 currency this vendor quotes in. Tier rows snapshot this value at insert time.</summary>
    string Currency,
    /// <summary>True when the vendor IS the part's manufacturer (direct-from-OEM source).</summary>
    bool IsManufacturer = false);
