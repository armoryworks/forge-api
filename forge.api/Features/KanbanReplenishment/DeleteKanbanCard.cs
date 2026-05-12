using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.KanbanReplenishment;

public record DeleteKanbanCardCommand(int Id) : IRequest;

public class DeleteKanbanCardHandler(AppDbContext db) : IRequestHandler<DeleteKanbanCardCommand>
{
    public async Task Handle(DeleteKanbanCardCommand command, CancellationToken cancellationToken)
    {
        var card = await db.KanbanCards
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Kanban card {command.Id} not found");

        card.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
    }
}
