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

        var userIds = revisions.Select(r => r.AuthoredById)
            .Concat(revisions.Where(r => r.ApprovedById != null).Select(r => r.ApprovedById!.Value))
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken);
        var fileIds = revisions.Select(r => r.FileAttachmentId).Distinct().ToList();
        var fileNames = await db.FileAttachments.AsNoTracking()
            .Where(f => fileIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FileName, cancellationToken);

        return revisions.Select(r => DocumentRevisionMapping.ToResponse(r) with
        {
            AuthoredByName = names.GetValueOrDefault(r.AuthoredById),
            ApprovedByName = r.ApprovedById is int approver ? names.GetValueOrDefault(approver) : null,
            FileName = fileNames.GetValueOrDefault(r.FileAttachmentId),
        }).ToList();
    }
}
