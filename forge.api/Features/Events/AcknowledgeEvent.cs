using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Events;

/// <summary>compliance-calendar A-4: record the current user's acknowledgement of a forced-ack event.</summary>
public record AcknowledgeEventCommand(int Id) : IRequest;

public class AcknowledgeEventHandler(AppDbContext db, IClock clock)
    : IRequestHandler<AcknowledgeEventCommand>
{
    public async Task Handle(AcknowledgeEventCommand request, CancellationToken cancellationToken)
    {
        var evt = await db.Events.FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Event {request.Id} not found");

        evt.AcknowledgedByUserId = db.CurrentUserId;
        evt.AcknowledgedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
