using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Ready → InProgress. Starts the step's dwell clock when the definition sets MaxDwellMinutes.</summary>
public record StartSequenceStepCommand(int InstanceId, string StepKey, int UserId) : IRequest<SequenceInstanceResponseModel>;

public class StartSequenceStepHandler(AppDbContext db, ISequenceEvaluationService evaluation, IClock clock)
    : IRequestHandler<StartSequenceStepCommand, SequenceInstanceResponseModel>
{
    public async Task<SequenceInstanceResponseModel> Handle(StartSequenceStepCommand request, CancellationToken cancellationToken)
    {
        var i = await SequenceStepCommands.LoadRunning(db, request.InstanceId, cancellationToken);
        var step = SequenceStepCommands.Step(i, request.StepKey);
        if (step.Status != SequenceStepStatus.Ready)
            throw new InvalidOperationException($"Step '{request.StepKey}' is {step.Status}; only Ready steps can start.");

        var now = clock.UtcNow;
        var def = i.Definition!.Steps.First(s => s.Key == step.StepKey);
        step.Status = SequenceStepStatus.InProgress;
        step.StartedAt = now;
        step.StartedByUserId = request.UserId;
        step.DwellExpiresAt = def.MaxDwellMinutes.HasValue ? now.AddMinutes(def.MaxDwellMinutes.Value) : null;
        step.DwellFiredAt = null;
        db.SequenceEvents.Add(SequenceEvaluator.Event(i, SequenceEventType.StepStarted, now, request.UserId, step.StepKey));
        db.LogActivityAt("sequence-step-started", $"Sequence step '{def.Name}' started", SequenceQueries.IndexingPoints(i));
        await db.SaveChangesAsync(cancellationToken);
        await evaluation.EvaluateAsync(i.Id, request.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(await db.SequenceInstances.WithGraph().FirstAsync(x => x.Id == i.Id, cancellationToken));
    }
}
