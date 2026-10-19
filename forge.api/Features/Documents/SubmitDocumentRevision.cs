using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

public record SubmitDocumentRevisionCommand(int DocumentId, int RevisionId) : IRequest<DocumentRevisionResponseModel>;

public class SubmitDocumentRevisionHandler(AppDbContext db)
    : IRequestHandler<SubmitDocumentRevisionCommand, DocumentRevisionResponseModel>
{
    public async Task<DocumentRevisionResponseModel> Handle(SubmitDocumentRevisionCommand request, CancellationToken cancellationToken)
    {
        var revision = await db.DocumentRevisions
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.Id == request.RevisionId && r.DocumentId == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Revision {request.RevisionId} not found on document {request.DocumentId}.");

        if (revision.Status != DocumentRevisionStatus.Draft)
            throw new InvalidOperationException("Only a draft revision can be submitted for review.");

        revision.Status = DocumentRevisionStatus.InReview;
        if (revision.Document.Status == ControlledDocumentStatus.Draft)
            revision.Document.Status = ControlledDocumentStatus.InReview;

        await db.SaveChangesAsync(cancellationToken);
        return DocumentRevisionMapping.ToResponse(revision);
    }
}
