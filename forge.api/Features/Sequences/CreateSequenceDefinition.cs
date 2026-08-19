using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Create a Draft definition (version 1 of a new code, or the next version if the code already exists). The whole graph is validated structurally on save.</summary>
public record CreateSequenceDefinitionCommand(SequenceDefinitionRequestModel Model) : IRequest<SequenceDefinitionResponseModel>;

public class CreateSequenceDefinitionHandler(AppDbContext db) : IRequestHandler<CreateSequenceDefinitionCommand, SequenceDefinitionResponseModel>
{
    public async Task<SequenceDefinitionResponseModel> Handle(CreateSequenceDefinitionCommand request, CancellationToken cancellationToken)
    {
        var m = request.Model;
        var code = (m.Code ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("A definition code is required.");
        if (string.IsNullOrWhiteSpace(m.Name)) throw new InvalidOperationException("A definition name is required.");

        var latest = await db.SequenceDefinitions.Where(d => d.Code == code && d.DeletedAt == null)
            .MaxAsync(d => (int?)d.Version, cancellationToken) ?? 0;

        var def = new SequenceDefinition { Code = code, Version = latest + 1, Status = SequenceDefinitionStatus.Draft };
        SequenceDefinitionGraph.Apply(def, m);

        var errors = SequenceNetValidator.Validate(def);
        if (errors.Count > 0) throw new InvalidOperationException("Invalid sequence definition: " + string.Join(" ", errors));

        db.SequenceDefinitions.Add(def);
        await db.SaveChangesAsync(cancellationToken); // need the id for the activity row
        db.LogActivityAt("sequence-definition-created", $"Sequence definition {code} v{def.Version} created (draft)", ("SequenceDefinition", def.Id));
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(def);
    }
}
