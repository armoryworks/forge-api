namespace Forge.Core.Models;

/// <summary>
/// Customer-create payload. Phase 3 F3 extends this from the original
/// name/companyName/email/phone shape so the customer-onboarding form can
/// capture credit limit, default tax/currency, and billing/shipping addresses
/// in a single POST instead of forcing a PATCH-after-create round trip.
///
/// All new fields are nullable / defaulted so existing callers continue to work.
/// </summary>
public record CreateCustomerRequestModel(
    string Name,
    string? CompanyName,
    string? Email,
    string? Phone,
    // F3 — full-record fields. All nullable so the minimal payload still works.
    decimal? CreditLimit = null,
    int? DefaultTaxCodeId = null,
    string? DefaultCurrency = null,
    AddressInput? BillingAddress = null,
    AddressInput? ShippingAddress = null,
    bool IsTaxExempt = false,
    string? TaxExemptionId = null);
