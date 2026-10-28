using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateCustomerRequestModel(
    string? Name,
    string? CompanyName,
    string? Email,
    string? Phone,
    bool? IsActive,
    // Phase 1r / Batch 15-16 — regulated-industry flags + reference-customer
    // consent. Each is independently nullable so a chip-toggle on the
    // customer detail panel only PATCHes the field it changed.
    bool? IsFdaRegulated = null,
    bool? IsAerospace = null,
    bool? IsAutomotive = null,
    bool? IsItarControlled = null,
    bool? IsReferenceOk = null,
    string? ReferenceNotes = null,
    // User-settable customer number. Supplying a changed value requires
    // customers.allow_manual_numbers to be on; validated for uniqueness.
    string? CustomerNumber = null,
    string? Fax = null,
    bool? IsTaxExempt = null,
    string? TaxExemptionId = null,
    decimal? CreditLimit = null,
    bool? ClearCreditLimit = null,
    string? DefaultCreditTerms = null,
    bool? ClearDefaultCreditTerms = null,
    int? CurrencyId = null,
    bool? ClearCurrency = null,
    FreightTerms? FreightTerms = null,
    bool? ClearFreightTerms = null,
    int? PaymentTermsId = null,
    bool? RequiresCoa = null,
    int? DefaultTaxCodeId = null,
    bool? ClearDefaultTaxCode = null,
    decimal? OverShipTolerancePct = null,
    decimal? UnderShipTolerancePct = null,
    bool? ClearOverShipTolerance = null,
    bool? ClearUnderShipTolerance = null);
