using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

public record RejectDocumentRevisionCommand(int DocumentId, int RevisionId) : IRequest<DocumentRevisionResponseModel>;

public class RejectDocumentRevisionHandler(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<RejectDocumentRevisionCommand, DocumentRevisionResponseModel>
{
    public async Task<DocumentRevisionResponseModel> Handle(RejectDocumentRevisionCommand request, CancellationToken cancellationToken)
    {
        var userId = DocumentRevisionMapping.CurrentUserId(httpContextAccessor);

        var document = await db.ControlledDocuments
            .Include(d => d.Revisions)
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found.");

        var revision = document.Revisions.FirstOrDefault(r => r.Id == request.RevisionId)
            ?? throw new KeyNotFoundException($"Revision {request.RevisionId} not found on document {request.DocumentId}.");

        if (revision.Status != DocumentRevisionStatus.InReview)
            throw new InvalidOperationException("Only a revision in review can be rejected.");

        revision.Status = DocumentRevisionStatus.Rejected;
        revision.ReviewedById = userId;

        if (document.Status == ControlledDocumentStatus.InReview
            && !document.Revisions.Any(r => r.Status == DocumentRevisionStatus.Approved))
            document.Status = ControlledDocumentStatus.Draft;

        await db.SaveChangesAsync(cancellationToken);
        return DocumentRevisionMapping.ToResponse(revision);
    }
}
