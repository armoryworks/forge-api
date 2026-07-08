using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Terms;

/// <summary>Entity → response-model projection shared by the Terms handlers.</summary>
public static class TermsDocumentMapper
{
    public static TermsDocumentResponseModel ToResponseModel(TermsDocument doc) => new(
        Id: doc.Id,
        Scope: doc.Scope.ToString(),
        CustomerId: doc.CustomerId,
        CustomerName: doc.Customer?.CompanyName ?? doc.Customer?.Name,
        PartId: doc.PartId,
        PartNumber: doc.Part?.PartNumber,
        Title: doc.Title,
        Summary: doc.Summary,
        BodyMarkdown: doc.BodyMarkdown,
        Version: doc.Version,
        EffectiveFrom: doc.EffectiveFrom,
        EffectiveTo: doc.EffectiveTo,
        IsActive: doc.IsActive,
        SortOrder: doc.SortOrder,
        SourceFileAttachmentId: doc.SourceFileAttachmentId,
        CreatedAt: doc.CreatedAt,
        UpdatedAt: doc.UpdatedAt);
}
