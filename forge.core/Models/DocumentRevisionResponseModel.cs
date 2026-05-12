using Forge.Core.Enums;

namespace Forge.Core.Models;

public record DocumentRevisionResponseModel(
    int Id,
    int DocumentId,
    int RevisionNumber,
    int FileAttachmentId,
    string ChangeDescription,
    int AuthoredById,
    int? ReviewedById,
    int? ApprovedById,
    DateTimeOffset? ApprovedAt,
    DocumentRevisionStatus Status);
