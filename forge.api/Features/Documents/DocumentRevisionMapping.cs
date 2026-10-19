using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Documents;

public static class DocumentRevisionMapping
{
    public static DocumentRevisionResponseModel ToResponse(DocumentRevision r) => new(
        r.Id,
        r.DocumentId,
        r.RevisionNumber,
        r.FileAttachmentId,
        r.ChangeDescription,
        r.AuthoredById,
        r.ReviewedById,
        r.ApprovedById,
        r.ApprovedAt,
        r.Status);

    public static int CurrentUserId(IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User not authenticated.");
        return int.Parse(userId);
    }
}
