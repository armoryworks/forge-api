using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>
/// Start a run of a Published definition (by id, or latest published by code) against an optional subject. Creates one
/// step instance and one gate instance per definition row, then evaluates once so start steps become Ready/Blocked.
/// </summary>
public record StartSequenceInstanceCommand(StartSequenceRequestModel Model, int UserId) : IRequest<SequenceInstanceResponseModel>;

public class StartSequenceInstanceHandler(AppDbContext db, ISequenceEvaluationService evaluation, IClock clock)
    : IRequestHandler<StartSequenceInstanceCommand, SequenceInstanceResponseModel>
{
    public async Task<SequenceInstanceResponseModel> Handle(StartSequenceInstanceCommand request, CancellationToken cancellationToken)
    {
        var m = request.Model;
        SequenceDefinition? def = null;
        if (m.DefinitionId.HasValue)
            def = await db.SequenceDefinitions.WithGraph().FirstOrDefaultAsync(d => d.Id == m.DefinitionId && d.DeletedAt == null, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(m.Code))
            def = await db.SequenceDefinitions.WithGraph()
                .Where(d => d.Code == m.Code && d.Status == SequenceDefinitionStatus.Published && d.DeletedAt == null)
                .OrderByDescending(d => d.Version).FirstOrDefaultAsync(cancellationToken);
        if (def is null) throw new KeyNotFoundException("No matching sequence definition.");
        if (def.Status != SequenceDefinitionStatus.Published)
            throw new InvalidOperationException($"Definition {def.Code} v{def.Version} is {def.Status}; only published definitions can start.");
        if (def.SubjectEntityType is not null && !string.IsNullOrEmpty(m.SubjectEntityType) && def.SubjectEntityType != m.SubjectEntityType)
            throw new InvalidOperationException($"Definition {def.Code} runs against {def.SubjectEntityType}, not {m.SubjectEntityType}.");

        var now = clock.UtcNow;
        var instance = new SequenceInstance
        {
            DefinitionId = def.Id,
            Definition = def,
            SubjectEntityType = string.IsNullOrWhiteSpace(m.SubjectEntityType) ? null : m.SubjectEntityType,
            SubjectEntityId = m.SubjectEntityId,
            Status = SequenceInstanceStatus.Running,
            StartedAt = now,
            StartedByUserId = request.UserId,
        };
        foreach (var s in def.Steps) instance.Steps.Add(new SequenceStepInstance { StepKey = s.Key, Status = SequenceStepStatus.Pending });
        foreach (var g in def.Gates) instance.Gates.Add(new SequenceGateInstance { StepKey = g.StepKey, GateKey = g.Key, Verdict = SequenceGateVerdict.Unknown });
        instance.Events.Add(SequenceEvaluator.Event(instance, SequenceEventType.InstanceStarted, now, request.UserId,
            payloadJson: $"{{\"definitionId\":{def.Id},\"code\":\"{def.Code}\",\"version\":{def.Version}}}"));

        db.SequenceInstances.Add(instance);
        await db.SaveChangesAsync(cancellationToken); // id needed for events + activity

        await evaluation.EvaluateAsync(instance.Id, request.UserId, cancellationToken);
        db.LogActivityAt("sequence-started", $"Sequence {def.Code} v{def.Version} started", SequenceQueries.IndexingPoints(instance));
        await db.SaveChangesAsync(cancellationToken);

        var fresh = await db.SequenceInstances.WithGraph().FirstAsync(i => i.Id == instance.Id, cancellationToken);
        return SequenceMapping.ToModel(fresh);
    }
}
