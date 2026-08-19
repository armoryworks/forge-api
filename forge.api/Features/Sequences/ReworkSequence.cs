using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>
/// Controlled back-edge: reset the target step and everything downstream of it to Pending (clearing completion,
/// clocks, manual clearances and overrides on those steps), record why, and re-evaluate. Reason required.
/// </summary>
public record ReworkSequenceCommand(int InstanceId, string TargetStepKey, string Reason, int UserId) : IRequest<SequenceInstanceResponseModel>;

public class ReworkSequenceHandler(AppDbContext db, ISequenceEvaluationService evaluation, IClock clock)
    : IRequestHandler<ReworkSequenceCommand, SequenceInstanceResponseModel>
{
    public async Task<SequenceInstanceResponseModel> Handle(ReworkSequenceCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("A rework reason is required.");
        var i = await SequenceStepCommands.LoadRunning(db, request.InstanceId, cancellationToken);
        var target = SequenceStepCommands.Step(i, request.TargetStepKey);
        var net = new SequenceNet(i.Definition!);
        var affected = net.Downstream(target.StepKey);
        affected.Add(target.StepKey);

        var now = clock.UtcNow;
        foreach (var step in i.Steps.Where(s => affected.Contains(s.StepKey)))
        {
            step.Status = SequenceStepStatus.Pending;
            step.ReadyAt = null; step.StartedAt = null; step.StartedByUserId = null;
            step.CompletedAt = null; step.CompletedByUserId = null; step.SkipReason = null;
            step.DwellExpiresAt = null; step.DwellFiredAt = null;
            db.SequenceEvents.Add(SequenceEvaluator.Event(i, SequenceEventType.StepReset, now, request.UserId, step.StepKey));
        }
        foreach (var gate in i.Gates.Where(g => affected.Contains(g.StepKey)))
        {
            gate.Verdict = SequenceGateVerdict.Unknown; gate.Reason = null; gate.LastEvaluatedAt = null;
            gate.ClearedAt = null; gate.ClearedByUserId = null;
            gate.OverriddenAt = null; gate.OverriddenByUserId = null; gate.OverrideReason = null;
        }
        db.SequenceEvents.Add(SequenceEvaluator.Event(i, SequenceEventType.Reworked, now, request.UserId, target.StepKey,
            payloadJson: $"{{\"reason\":\"{request.Reason.Trim().Replace("\"", "\\\"")}\",\"resetSteps\":[{string.Join(",", affected.OrderBy(k => k).Select(k => $"\"{k}\""))}]}}"));
        db.LogActivityAt("sequence-reworked", $"Sequence reworked from '{target.StepKey}': {request.Reason.Trim()}", SequenceQueries.IndexingPoints(i));
        await db.SaveChangesAsync(cancellationToken);
        await evaluation.EvaluateAsync(i.Id, request.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(await db.SequenceInstances.WithGraph().FirstAsync(x => x.Id == i.Id, cancellationToken));
    }
}
