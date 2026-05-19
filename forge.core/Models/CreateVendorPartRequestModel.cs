namespace Forge.Core.Models;

/// <summary>
/// Pillar 3 — Body for POST /api/v1/vendor-parts. VendorId + PartId are
/// required and immutable post-create; sourcing metadata fields are optional.
/// </summary>
public record CreateVendorPartRequestModel(
    int VendorId,
    int PartId,
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
    /// <summary>ISO-4217 currency code. Optional; defaults to "USD".</summary>
    string? Currency = null,
    /// <summary>
    /// True when the vendor IS the part's manufacturer (direct-from-OEM).
    /// Handler normalizes: ManufacturerName cleared (rendered from vendor on
    /// read) and VendorPartNumber / VendorMpn mirrored to a single identifier.
    /// </summary>
    bool IsManufacturer = false);
