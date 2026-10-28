namespace Forge.Core.Models;

/// <summary>
/// Whether the caller may override a document's tax rate for this customer, and the Verified,
/// unexpired CustomerTaxDocument on file (if any) that the certificate-backed exemption reasons need.
/// </summary>
public record CustomerTaxEditabilityResponseModel(
    bool CanEditTax,
    string? Reason,
    int? ActiveDocumentId,
    string? StateCode,
    DateTimeOffset? ExpiresAt);
