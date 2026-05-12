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
    string Currency);
