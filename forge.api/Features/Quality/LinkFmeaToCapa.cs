using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record LinkFmeaToCapaCommand(int FmeaId, int ItemId, int CapaId) : IRequest;

public class LinkFmeaToCapaHandler(AppDbContext db)
    : IRequestHandler<LinkFmeaToCapaCommand>
{
    public async Task Handle(LinkFmeaToCapaCommand command, CancellationToken cancellationToken)
    {
        var item = await db.Set<FmeaItem>()
            .FirstOrDefaultAsync(i => i.Id == command.ItemId && i.FmeaId == command.FmeaId, cancellationToken)
            ?? throw new KeyNotFoundException($"FMEA item {command.ItemId} not found");

        var capa = await db.CorrectiveActions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.CapaId, cancellationToken)
            ?? throw new KeyNotFoundException($"CAPA {command.CapaId} not found");

        item.CapaId = command.CapaId;
        await db.SaveChangesAsync(cancellationToken);
    }
}
