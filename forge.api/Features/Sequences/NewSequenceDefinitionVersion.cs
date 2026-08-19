using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Copy a definition into a new Draft version (Version = max+1) so it can be edited and published without touching in-flight runs.</summary>
public record NewSequenceDefinitionVersionCommand(int Id) : IRequest<SequenceDefinitionResponseModel>;

public class NewSequenceDefinitionVersionHandler(AppDbContext db) : IRequestHandler<NewSequenceDefinitionVersionCommand, SequenceDefinitionResponseModel>
{
    public async Task<SequenceDefinitionResponseModel> Handle(NewSequenceDefinitionVersionCommand request, CancellationToken cancellationToken)
    {
        var src = await db.SequenceDefinitions.WithGraph().FirstOrDefaultAsync(d => d.Id == request.Id && d.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Sequence definition {request.Id} not found.");
        var latest = await db.SequenceDefinitions.Where(d => d.Code == src.Code && d.DeletedAt == null).MaxAsync(d => d.Version, cancellationToken);
        if (await db.SequenceDefinitions.AnyAsync(d => d.Code == src.Code && d.Status == SequenceDefinitionStatus.Draft && d.DeletedAt == null, cancellationToken))
            throw new InvalidOperationException($"{src.Code} already has a draft version; edit or publish it first.");

        var copy = new SequenceDefinition
        {
            Code = src.Code, Version = latest + 1, Name = src.Name, Description = src.Description,
            SubjectEntityType = src.SubjectEntityType, Status = SequenceDefinitionStatus.Draft,
        };
        foreach (var s in src.Steps) copy.Steps.Add(new SequenceStepDefinition
        {
            Key = s.Key, Name = s.Name, Description = s.Description, SortOrder = s.SortOrder, JoinPolicy = s.JoinPolicy,
            MaxDwellMinutes = s.MaxDwellMinutes, DwellExpiryAction = s.DwellExpiryAction, EscalateRole = s.EscalateRole,
        });
        foreach (var e in src.Edges) copy.Edges.Add(new SequenceEdgeDefinition { FromStepKey = e.FromStepKey, ToStepKey = e.ToStepKey, IsRework = e.IsRework });
        foreach (var g in src.Gates) copy.Gates.Add(new SequenceGateDefinition
        {
            StepKey = g.StepKey, Key = g.Key, Name = g.Name, SourceType = g.SourceType, ConfigJson = g.ConfigJson,
            ExpiryAction = g.ExpiryAction, EscalateRole = g.EscalateRole,
        });

        db.SequenceDefinitions.Add(copy);
        db.LogActivityAt("sequence-definition-versioned", $"Sequence definition {copy.Code} v{copy.Version} drafted from v{src.Version}", ("SequenceDefinition", src.Id));
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(copy);
    }
}
