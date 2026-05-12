using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record DeletePartAlternateCommand(int PartId, int AlternateId) : IRequest;

public class DeletePartAlternateHandler(AppDbContext db) : IRequestHandler<DeletePartAlternateCommand>
{
    public async Task Handle(DeletePartAlternateCommand request, CancellationToken cancellationToken)
    {
        var alternate = await db.PartAlternates
            .FirstOrDefaultAsync(a => a.Id == request.AlternateId && a.PartId == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part alternate {request.AlternateId} not found");

        db.PartAlternates.Remove(alternate);
        await db.SaveChangesAsync(cancellationToken);
    }
}
