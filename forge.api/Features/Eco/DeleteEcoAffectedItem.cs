using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record DeleteEcoAffectedItemCommand(int EcoId, int ItemId) : IRequest;

public class DeleteEcoAffectedItemHandler(AppDbContext db) : IRequestHandler<DeleteEcoAffectedItemCommand>
{
    public async Task Handle(DeleteEcoAffectedItemCommand request, CancellationToken cancellationToken)
    {
        var item = await db.EcoAffectedItems
            .FirstOrDefaultAsync(a => a.Id == request.ItemId && a.EcoId == request.EcoId, cancellationToken)
            ?? throw new KeyNotFoundException($"Affected item {request.ItemId} not found");

        db.EcoAffectedItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }
}
