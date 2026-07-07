namespace Forge.Core.Models;

public record CustomerTaxDocumentResponseModel(
    int Id,
    int FileAttachmentId,
    string FileName,
    string? StateCode,
    string CertificateType,
    string? CertificateNumber,
    string Status,
    DateTimeOffset? VerifiedAt,
    string? VerifiedByName,
    DateTimeOffset? ExpirationDate,
    string? RejectionReason);
