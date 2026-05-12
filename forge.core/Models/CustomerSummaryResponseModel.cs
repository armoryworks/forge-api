namespace Forge.Core.Models;

public record CustomerSummaryResponseModel(
    int Id,
    string Name,
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
    string? ReferenceNotes = null);
