namespace Forge.Core.Models;

/// <summary>
/// Wave 2 — richer convert-lead payload. Captures the customer-required
/// fields the user fills in via the convert-lead stepper so the resulting
/// Customer is fully populated in one atomic operation, rather than a
/// shell record that needs immediate follow-up patches.
///
/// All the customer-side fields are optional — the simplest call is
/// just <c>{ createJob: false }</c>, which preserves the pre-Wave-2
/// behaviour. The stepper sends the populated fields when the user
/// fills them; partial fills are accepted (e.g. billing-only without
/// shipping) and addresses are only created when the input block is
/// present.
/// </summary>
public record ConvertLeadRequestModel(
    bool CreateJob,
    decimal? CreditLimit = null,
    bool? IsTaxExempt = null,
    string? TaxExemptionId = null,
    int? DefaultTaxCodeId = null,
    string? DefaultCurrency = null,
    AddressInput? BillingAddress = null,
    AddressInput? ShippingAddress = null);
