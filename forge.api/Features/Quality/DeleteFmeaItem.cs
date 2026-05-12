using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record DeleteFmeaItemCommand(int FmeaId, int ItemId) : IRequest;

public class DeleteFmeaItemHandler(AppDbContext db)
    : IRequestHandler<DeleteFmeaItemCommand>
{
    public async Task Handle(DeleteFmeaItemCommand command, CancellationToken cancellationToken)
    {
        var item = await db.Set<FmeaItem>()
            .FirstOrDefaultAsync(i => i.Id == command.ItemId && i.FmeaId == command.FmeaId, cancellationToken)
            ?? throw new KeyNotFoundException($"FMEA item {command.ItemId} not found");

        db.Set<FmeaItem>().Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }
}
