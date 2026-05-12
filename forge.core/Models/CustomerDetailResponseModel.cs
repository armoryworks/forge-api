namespace Forge.Core.Models;

public record CustomerDetailResponseModel(
    int Id,
    string Name,
    string? CompanyName,
    string? Email,
    string? Phone,
    bool IsActive,
    bool IsTaxExempt,
    string? TaxExemptionId,
    string? ExternalId,
    string? ExternalRef,
    string? Provider,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<ContactResponseModel> Contacts,
    List<CustomerJobSummaryModel> Jobs,
    // F3 — full-record fields surfaced on the detail GET so callers that
    // POSTed a complete record can verify it round-tripped.
    decimal? CreditLimit = null,
    int? DefaultTaxCodeId = null,
    string? DefaultCurrency = null,
    AddressOutput? BillingAddress = null,
    AddressOutput? ShippingAddress = null,
    // Phase 1r / Batch 15-16 — regulated-industry flags + reference-customer consent.
    bool IsFdaRegulated = false,
    bool IsAerospace = false,
    bool IsAutomotive = false,
    bool IsItarControlled = false,
    bool IsReferenceOk = false,
    string? ReferenceNotes = null);

/// <summary>
/// Minimal nested address shape returned alongside a customer record. Phase 3 F3.
/// Mirrors <see cref="AddressInput"/> so callers can round-trip the payload.
/// </summary>
public record AddressOutput(
    string Street,
    string? Line2,
    string City,
    string State,
    string Postal,
    string? Country);
