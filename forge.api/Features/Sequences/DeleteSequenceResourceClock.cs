using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Soft-delete a resource clock (e.g. the lot was consumed / the permit renewed).</summary>
public record DeleteSequenceResourceClockCommand(int Id) : IRequest;

public class DeleteSequenceResourceClockHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteSequenceResourceClockCommand>
{
    public async Task Handle(DeleteSequenceResourceClockCommand request, CancellationToken cancellationToken)
    {
        var c = await db.SequenceResourceClocks.FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Resource clock {request.Id} not found.");
        c.DeletedAt = clock.UtcNow;
        db.LogActivityAt("sequence-clock-removed", "Clock removed", (c.ResourceType, c.ResourceId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
