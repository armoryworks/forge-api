using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Events;

public record DeleteEventCommand(int Id) : IRequest;

public class DeleteEventHandler(AppDbContext db)
    : IRequestHandler<DeleteEventCommand>
{
    public async Task Handle(DeleteEventCommand request, CancellationToken cancellationToken)
    {
        var evt = await db.Events
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Event {request.Id} not found");

        // Soft cancel rather than hard delete
        evt.IsCancelled = true;
        await db.SaveChangesAsync(cancellationToken);
    }
}
