using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Replace a DRAFT definition's graph. Published/Retired definitions are immutable — use new-version.</summary>
public record UpdateSequenceDefinitionCommand(int Id, SequenceDefinitionRequestModel Model) : IRequest<SequenceDefinitionResponseModel>;

public class UpdateSequenceDefinitionHandler(AppDbContext db) : IRequestHandler<UpdateSequenceDefinitionCommand, SequenceDefinitionResponseModel>
{
    public async Task<SequenceDefinitionResponseModel> Handle(UpdateSequenceDefinitionCommand request, CancellationToken cancellationToken)
    {
        var def = await db.SequenceDefinitions.WithGraph().FirstOrDefaultAsync(d => d.Id == request.Id && d.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Sequence definition {request.Id} not found.");
        if (def.Status != SequenceDefinitionStatus.Draft)
            throw new InvalidOperationException("Only draft definitions can be edited; create a new version instead.");
        if (!string.Equals(def.Code, request.Model.Code?.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("A definition's code cannot change.");

        db.SequenceStepDefinitions.RemoveRange(def.Steps);
        db.SequenceEdgeDefinitions.RemoveRange(def.Edges);
        db.SequenceGateDefinitions.RemoveRange(def.Gates);
        SequenceDefinitionGraph.Apply(def, request.Model);

        var errors = SequenceNetValidator.Validate(def);
        if (errors.Count > 0) throw new InvalidOperationException("Invalid sequence definition: " + string.Join(" ", errors));

        db.LogActivityAt("sequence-definition-updated", $"Sequence definition {def.Code} v{def.Version} updated", ("SequenceDefinition", def.Id));
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(def);
    }
}
