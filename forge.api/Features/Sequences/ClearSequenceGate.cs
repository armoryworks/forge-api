using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Record a manual clearance on a ManualClearance gate — the record IS the sign-off. Idempotent.</summary>
public record ClearSequenceGateCommand(int InstanceId, string StepKey, string GateKey, int UserId) : IRequest<SequenceInstanceResponseModel>;

public class ClearSequenceGateHandler(AppDbContext db, ISequenceEvaluationService evaluation, IClock clock)
    : IRequestHandler<ClearSequenceGateCommand, SequenceInstanceResponseModel>
{
    public async Task<SequenceInstanceResponseModel> Handle(ClearSequenceGateCommand request, CancellationToken cancellationToken)
    {
        var i = await SequenceStepCommands.LoadRunning(db, request.InstanceId, cancellationToken);
        var gate = SequenceStepCommands.Gate(i, request.StepKey, request.GateKey);
        var def = i.Definition!.Gates.First(g => g.StepKey == gate.StepKey && g.Key == gate.GateKey);
        if (def.SourceType != SequenceGateSourceType.ManualClearance)
            throw new InvalidOperationException($"Gate '{def.Name}' is a {def.SourceType} gate; only ManualClearance gates are cleared by hand (use override to force others).");

        if (!gate.ClearedAt.HasValue)
        {
            var now = clock.UtcNow;
            gate.ClearedAt = now;
            gate.ClearedByUserId = request.UserId;
            db.SequenceEvents.Add(SequenceEvaluator.Event(i, SequenceEventType.GateCleared, now, request.UserId, gate.StepKey, gate.GateKey));
            db.LogActivityAt("sequence-gate-cleared", $"Gate '{def.Name}' cleared", SequenceQueries.IndexingPoints(i));
            await db.SaveChangesAsync(cancellationToken);
        }
        await evaluation.EvaluateAsync(i.Id, request.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(await db.SequenceInstances.WithGraph().FirstAsync(x => x.Id == i.Id, cancellationToken));
    }
}
