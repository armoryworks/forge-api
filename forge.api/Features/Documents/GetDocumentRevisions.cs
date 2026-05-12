using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

public record GetDocumentRevisionsQuery(int DocumentId) : IRequest<List<DocumentRevisionResponseModel>>;

public class GetDocumentRevisionsHandler(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<GetDocumentRevisionsQuery, List<DocumentRevisionResponseModel>>
{
    public async Task<List<DocumentRevisionResponseModel>> Handle(GetDocumentRevisionsQuery request, CancellationToken cancellationToken)
    {
        _ = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        var exists = await db.ControlledDocuments.AnyAsync(d => d.Id == request.DocumentId, cancellationToken);
        if (!exists)
            throw new KeyNotFoundException($"Document {request.DocumentId} not found.");

        var revisions = await db.DocumentRevisions
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId)
            .OrderByDescending(r => r.RevisionNumber)
            .ToListAsync(cancellationToken);

        return revisions.Select(r => new DocumentRevisionResponseModel(
            r.Id,
            r.DocumentId,
            r.RevisionNumber,
            r.FileAttachmentId,
            r.ChangeDescription,
            r.AuthoredById,
            r.ReviewedById,
            r.ApprovedById,
            r.ApprovedAt,
            r.Status)).ToList();
    }
}
