using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

public record ApproveDocumentRevisionCommand(int DocumentId, int RevisionId) : IRequest<DocumentRevisionResponseModel>;

public class ApproveDocumentRevisionHandler(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor,
    IClock clock) : IRequestHandler<ApproveDocumentRevisionCommand, DocumentRevisionResponseModel>
{
    public async Task<DocumentRevisionResponseModel> Handle(ApproveDocumentRevisionCommand request, CancellationToken cancellationToken)
    {
        var userId = DocumentRevisionMapping.CurrentUserId(httpContextAccessor);

        var document = await db.ControlledDocuments
            .Include(d => d.Revisions)
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found.");

        var revision = document.Revisions.FirstOrDefault(r => r.Id == request.RevisionId)
            ?? throw new KeyNotFoundException($"Revision {request.RevisionId} not found on document {request.DocumentId}.");

        if (revision.Status != DocumentRevisionStatus.InReview)
            throw new InvalidOperationException("Only a revision in review can be approved.");

        var now = clock.UtcNow;

        foreach (var previous in document.Revisions.Where(r => r.Id != revision.Id && r.Status == DocumentRevisionStatus.Approved))
            previous.Status = DocumentRevisionStatus.Superseded;

        revision.Status = DocumentRevisionStatus.Approved;
        revision.ReviewedById = userId;
        revision.ApprovedById = userId;
        revision.ApprovedAt = now;

        document.CurrentRevision = revision.RevisionNumber;
        document.Status = ControlledDocumentStatus.Released;
        document.ReleasedAt = now;
        document.ReviewDueDate = now.AddDays(document.ReviewIntervalDays);

        await db.SaveChangesAsync(cancellationToken);
        return DocumentRevisionMapping.ToResponse(revision);
    }
}
