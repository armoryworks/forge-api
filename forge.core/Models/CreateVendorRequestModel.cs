namespace Forge.Core.Models;

public record CreateVendorRequestModel(
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
    // Marks the vendor as a 1099 payee and captures its taxpayer id for filing.
    bool Is1099 = false,
    string? TaxId = null,
    // Optional caller-supplied vendor number. Honoured only when
    // vendors.allow_manual_numbers is on; otherwise auto-generated (VEND-#####).
    string? VendorNumber = null);
