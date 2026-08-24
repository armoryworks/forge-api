namespace Forge.Core.Models;

public record UpdateVendorRequestModel(
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
    // 1099 payee flag + taxpayer id. Null leaves the stored value untouched.
    bool? Is1099 = null,
    string? TaxId = null,
    // User-settable vendor number. Supplying a changed value requires
    // vendors.allow_manual_numbers to be on; validated for uniqueness.
    string? VendorNumber = null);
