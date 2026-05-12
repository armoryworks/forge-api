using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record VerifyIdentityDocumentCommand(int DocumentId, int VerifiedById) : IRequest;

public class VerifyIdentityDocumentHandler(AppDbContext db)
    : IRequestHandler<VerifyIdentityDocumentCommand>
{
    public async Task Handle(VerifyIdentityDocumentCommand request, CancellationToken ct)
    {
        var document = await db.IdentityDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, ct)
            ?? throw new KeyNotFoundException($"Identity document {request.DocumentId} not found.");

        document.VerifiedAt = DateTimeOffset.UtcNow;
        document.VerifiedById = request.VerifiedById;

        await db.SaveChangesAsync(ct);
    }
}
