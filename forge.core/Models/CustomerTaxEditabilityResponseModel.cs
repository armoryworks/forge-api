namespace Forge.Core.Models;

/// <summary>
/// S1 — whether a quote's tax rate may be overridden for this customer.
/// Editable only when a Verified, unexpired CustomerTaxDocument is on file.
/// </summary>
public record CustomerTaxEditabilityResponseModel(
    bool CanEditTax,
    string? Reason,
    int? ActiveDocumentId,
    string? StateCode,
    DateTimeOffset? ExpiresAt);
