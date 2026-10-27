namespace Forge.Core.Models;

/// <summary>The current approved revision of a work instruction that applies to an operation (linked to the operation, or to its part with no operation).</summary>
public record WorkInstructionResponseModel(
    int DocumentId,
    string DocumentNumber,
    string Title,
    string? Description,
    int RevisionId,
    int RevisionNumber,
    int FileAttachmentId,
    string FileName,
    string ContentType,
    DateTimeOffset? ApprovedAt,
    int? PartId,
    int? OperationId);
