namespace Forge.Core.Models;

/// <summary>
/// Pillar 3 — Body for PUT /api/v1/vendor-parts/{id}. VendorId / PartId are
/// omitted because the (Vendor, Part) pair is immutable once created — to
/// re-source a part to a different vendor, delete this row and create a new
/// one. All other sourcing metadata fields are individually patch-able.
/// </summary>
public record UpdateVendorPartRequestModel(
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
    /// <summary>ISO-4217 currency code. Null = leave existing unchanged.</summary>
    string? Currency = null);
