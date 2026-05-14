
namespace Forge.Core.Models;

public record UploadIdentityDocumentRequestModel(
    IdentityDocumentType DocumentType,
    DateTimeOffset? ExpiresAt);
