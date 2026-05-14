
namespace Forge.Core.Models;

public record IdentityDocumentResponseModel(
    int Id,
    int UserId,
    IdentityDocumentType DocumentType,
    int FileAttachmentId,
    string FileName,
    DateTimeOffset? VerifiedAt,
    int? VerifiedById,
    string? VerifiedByName,
    DateTimeOffset? ExpiresAt,
    string? Notes,
    DateTimeOffset CreatedAt);
