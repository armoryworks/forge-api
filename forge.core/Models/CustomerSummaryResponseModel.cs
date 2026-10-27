using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CustomerSummaryResponseModel(
    int Id,
    string Name,
    string? CustomerNumber,
    string? CompanyName,
    string? Email,
    string? Phone,
    bool IsActive,
    string? ExternalId,
    string? ExternalRef,
    string? Provider,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int EstimateCount,
    int QuoteCount,
    int OrderCount,
    int ActiveJobCount,
    int OpenInvoiceCount,
    decimal OpenInvoiceTotal,
    decimal YtdRevenue,
    // Phase 1r / Batch 15-16 — regulated-industry flags + reference-customer
    // consent. Surfaced on the summary so the customer-detail Overview tab
    // can render the toggles without a separate detail load.
    bool IsFdaRegulated = false,
    bool IsAerospace = false,
    bool IsAutomotive = false,
    bool IsItarControlled = false,
    bool IsReferenceOk = false,
    string? ReferenceNotes = null,
    decimal? CreditLimit = null,
    bool IsTaxExempt = false,
    string? TaxExemptionId = null,
    uint RowVersion = 0,
    string? DefaultCreditTerms = null,
    bool IsOnCreditHold = false,
    int? CurrencyId = null,
    bool CurrencyLocked = false,
    FreightTerms? FreightTerms = null,
    int? PaymentTermsId = null,
    PaymentTermsModel? PaymentTerms = null,
    bool RequiresCoa = false,
    int? DefaultTaxCodeId = null);
