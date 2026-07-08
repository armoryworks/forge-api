using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Middleware;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Terms;

/// <summary>
/// S3 — soft-delete a terms document (DeletedAt stamp; the global query
/// filter hides it from compilation and CRUD reads). Existing
/// QuoteTermsSnapshots are untouched — they carry their own compiled HTML.
/// </summary>
public record DeleteTermsDocumentCommand(int Id, bool CallerIsAdmin) : IRequest;

public class DeleteTermsDocumentHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteTermsDocumentCommand>
{
    public async Task Handle(DeleteTermsDocumentCommand request, CancellationToken ct)
    {
        var doc = await db.TermsDocuments
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Terms document {request.Id} not found");

        if (doc.Scope == TermsScope.Company && !request.CallerIsAdmin)
            throw new ForbiddenException("Only administrators can manage company-scope terms");

        doc.DeletedAt = clock.UtcNow;

        if (doc.CustomerId.HasValue)
        {
            db.LogActivityAt("terms-document-removed",
                $"Removed terms document: {doc.Title}",
                ("Customer", doc.CustomerId.Value));
        }
        else if (doc.PartId.HasValue)
        {
            db.LogActivityAt("terms-document-removed",
                $"Removed terms document: {doc.Title}",
                ("Part", doc.PartId.Value));
        }
        // Company scope: no anchor entity — see CreateTermsDocumentHandler.

        await db.SaveChangesAsync(ct);
    }
}
