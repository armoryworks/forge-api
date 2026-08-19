using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Draft → Published (immutable, startable). Any older Published version of the same code is Retired.</summary>
public record PublishSequenceDefinitionCommand(int Id, int UserId) : IRequest<SequenceDefinitionResponseModel>;

public class PublishSequenceDefinitionHandler(AppDbContext db, IClock clock) : IRequestHandler<PublishSequenceDefinitionCommand, SequenceDefinitionResponseModel>
{
    public async Task<SequenceDefinitionResponseModel> Handle(PublishSequenceDefinitionCommand request, CancellationToken cancellationToken)
    {
        var def = await db.SequenceDefinitions.WithGraph().FirstOrDefaultAsync(d => d.Id == request.Id && d.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Sequence definition {request.Id} not found.");
        if (def.Status != SequenceDefinitionStatus.Draft)
            throw new InvalidOperationException($"Definition is {def.Status}; only drafts can be published.");

        var errors = SequenceNetValidator.Validate(def);
        if (errors.Count > 0) throw new InvalidOperationException("Cannot publish an invalid definition: " + string.Join(" ", errors));

        var older = await db.SequenceDefinitions
            .Where(d => d.Code == def.Code && d.Id != def.Id && d.Status == SequenceDefinitionStatus.Published && d.DeletedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var o in older) o.Status = SequenceDefinitionStatus.Retired;

        def.Status = SequenceDefinitionStatus.Published;
        def.PublishedAt = clock.UtcNow;
        def.PublishedByUserId = request.UserId;
        db.LogActivityAt("sequence-definition-published", $"Sequence definition {def.Code} v{def.Version} published", ("SequenceDefinition", def.Id));
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(def);
    }
}
