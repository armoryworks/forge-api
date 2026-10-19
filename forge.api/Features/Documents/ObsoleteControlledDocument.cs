using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

public record ObsoleteControlledDocumentCommand(int DocumentId) : IRequest;

public class ObsoleteControlledDocumentHandler(AppDbContext db) : IRequestHandler<ObsoleteControlledDocumentCommand>
{
    public async Task Handle(ObsoleteControlledDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.ControlledDocuments
            .Include(d => d.Revisions)
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found.");

        if (document.Status == ControlledDocumentStatus.Obsolete)
            throw new InvalidOperationException("The document is already obsolete.");

        if (document.Revisions.Any(r => r.Status is DocumentRevisionStatus.Draft or DocumentRevisionStatus.InReview))
            throw new InvalidOperationException("Resolve the revision in draft or review before making the document obsolete.");

        document.Status = ControlledDocumentStatus.Obsolete;
        await db.SaveChangesAsync(cancellationToken);
    }
}
