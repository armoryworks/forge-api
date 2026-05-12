using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record DeleteIdentityDocumentCommand(int UserId, int DocumentId) : IRequest;

public class DeleteIdentityDocumentHandler(AppDbContext db)
    : IRequestHandler<DeleteIdentityDocumentCommand>
{
    public async Task Handle(DeleteIdentityDocumentCommand request, CancellationToken ct)
    {
        var document = await db.IdentityDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.UserId == request.UserId, ct)
            ?? throw new KeyNotFoundException(
                $"Identity document {request.DocumentId} not found or not owned by user {request.UserId}.");

        document.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
