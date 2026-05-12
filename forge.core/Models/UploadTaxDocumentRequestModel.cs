using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UploadTaxDocumentRequestModel(
    TaxDocumentType DocumentType,
    int TaxYear,
    int FileAttachmentId);
