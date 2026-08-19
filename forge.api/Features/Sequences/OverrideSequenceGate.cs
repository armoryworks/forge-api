using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Force a gate to Go with a mandatory reason. Sticky until the step is reset by rework. Fully audited.</summary>
public record OverrideSequenceGateCommand(int InstanceId, string StepKey, string GateKey, string Reason, int UserId) : IRequest<SequenceInstanceResponseModel>;

public class OverrideSequenceGateHandler(AppDbContext db, ISequenceEvaluationService evaluation, IClock clock)
    : IRequestHandler<OverrideSequenceGateCommand, SequenceInstanceResponseModel>
{
    public async Task<SequenceInstanceResponseModel> Handle(OverrideSequenceGateCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("An override reason is required.");
        var i = await SequenceStepCommands.LoadRunning(db, request.InstanceId, cancellationToken);
        var gate = SequenceStepCommands.Gate(i, request.StepKey, request.GateKey);
        var def = i.Definition!.Gates.First(g => g.StepKey == gate.StepKey && g.Key == gate.GateKey);

        var now = clock.UtcNow;
        gate.OverriddenAt = now;
        gate.OverriddenByUserId = request.UserId;
        gate.OverrideReason = request.Reason.Trim();
        db.SequenceEvents.Add(SequenceEvaluator.Event(i, SequenceEventType.GateOverridden, now, request.UserId, gate.StepKey, gate.GateKey,
            $"{{\"reason\":\"{gate.OverrideReason.Replace("\"", "\\\"")}\"}}"));
        db.LogActivityAt("sequence-gate-overridden", $"Gate '{def.Name}' overridden: {gate.OverrideReason}", SequenceQueries.IndexingPoints(i));
        await db.SaveChangesAsync(cancellationToken);
        await evaluation.EvaluateAsync(i.Id, request.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(await db.SequenceInstances.WithGraph().FirstAsync(x => x.Id == i.Id, cancellationToken));
    }
}
