using MediatR;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Attach a clock to a resource (lot, permit, sample...). It travels with the resource; ResourceClock gates read it.</summary>
public record CreateSequenceResourceClockCommand(SequenceResourceClockRequestModel Model) : IRequest<SequenceResourceClockResponseModel>;

public class CreateSequenceResourceClockHandler(AppDbContext db, IClock clock) : IRequestHandler<CreateSequenceResourceClockCommand, SequenceResourceClockResponseModel>
{
    public async Task<SequenceResourceClockResponseModel> Handle(CreateSequenceResourceClockCommand request, CancellationToken cancellationToken)
    {
        var m = request.Model;
        if (string.IsNullOrWhiteSpace(m.ResourceType)) throw new InvalidOperationException("A resource type is required.");
        var c = new SequenceResourceClock
        {
            ResourceType = m.ResourceType.Trim(), ResourceId = m.ResourceId, ExpiresAt = m.ExpiresAt,
            ExpiryAction = m.ExpiryAction, EscalateRole = m.EscalateRole, Note = m.Note,
        };
        db.SequenceResourceClocks.Add(c);
        db.LogActivityAt("sequence-clock-set", $"Clock set: expires {c.ExpiresAt:u} ({c.ExpiryAction})", (c.ResourceType, c.ResourceId));
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(c, clock.UtcNow);
    }
}
