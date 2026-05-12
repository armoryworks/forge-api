namespace Forge.Core.Models;

public record FileAttachmentResponseModel(
    int Id,
    string FileName,
    string ContentType,
    long Size,
    string Url,
    string EntityType,
    int EntityId,
    int UploadedById,
    string UploadedByName,
    DateTimeOffset CreatedAt,
    int? PartRevisionId,
    string? RequiredRole);
