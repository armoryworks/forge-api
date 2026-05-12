using Forge.Core.Enums;

namespace Forge.Core.Models;

public record TaxDocumentResponseModel(
    int Id,
    int UserId,
    TaxDocumentType DocumentType,
    int TaxYear,
    string? EmployerName,
    int? FileAttachmentId,
    PayrollDocumentSource Source,
    string? ExternalId);
