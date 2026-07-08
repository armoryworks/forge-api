using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateTermsDocumentRequestModel(
    TermsScope Scope,
    int? CustomerId,
    int? PartId,
    string Title,
    string? Summary,
    string BodyMarkdown,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive = true,
    int SortOrder = 0,
    int? SourceFileAttachmentId = null);
