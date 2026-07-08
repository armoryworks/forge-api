namespace Forge.Core.Models;

/// <summary>
/// Full-replace update payload. Scope / CustomerId / PartId are immutable
/// post-create (same contract as VendorPart's Vendor/Part FKs) — create a new
/// document to re-scope. A BodyMarkdown change bumps <c>Version</c>.
/// </summary>
public record UpdateTermsDocumentRequestModel(
    string Title,
    string? Summary,
    string BodyMarkdown,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive,
    int SortOrder,
    int? SourceFileAttachmentId);
