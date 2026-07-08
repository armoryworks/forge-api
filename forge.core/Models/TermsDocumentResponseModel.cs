namespace Forge.Core.Models;

public record TermsDocumentResponseModel(
    int Id,
    string Scope,
    int? CustomerId,
    string? CustomerName,
    int? PartId,
    string? PartNumber,
    string Title,
    string? Summary,
    string BodyMarkdown,
    int Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive,
    int SortOrder,
    int? SourceFileAttachmentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
