namespace Forge.Core.Models;

public record CreateCustomerTaxDocumentRequestModel(
    int FileAttachmentId,
    string StateCode,
    string CertificateType,
    string? CertificateNumber,
    DateTimeOffset? ExpirationDate);
