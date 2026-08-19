using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Retire a definition (no new runs; in-flight runs continue). Drafts are soft-deleted outright.</summary>
public record RetireSequenceDefinitionCommand(int Id) : IRequest;

public class RetireSequenceDefinitionHandler(AppDbContext db, IClock clock) : IRequestHandler<RetireSequenceDefinitionCommand>
{
    public async Task Handle(RetireSequenceDefinitionCommand request, CancellationToken cancellationToken)
    {
        var def = await db.SequenceDefinitions.FirstOrDefaultAsync(d => d.Id == request.Id && d.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Sequence definition {request.Id} not found.");
        if (def.Status == SequenceDefinitionStatus.Draft) def.DeletedAt = clock.UtcNow;
        else def.Status = SequenceDefinitionStatus.Retired;
        db.LogActivityAt("sequence-definition-retired", $"Sequence definition {def.Code} v{def.Version} retired", ("SequenceDefinition", def.Id));
        await db.SaveChangesAsync(cancellationToken);
    }
}
