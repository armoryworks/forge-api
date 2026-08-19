using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>InProgress (or Ready — a zero-duration step) → Complete, then re-evaluate so successors advance.</summary>
public record CompleteSequenceStepCommand(int InstanceId, string StepKey, int UserId) : IRequest<SequenceInstanceResponseModel>;

public class CompleteSequenceStepHandler(AppDbContext db, ISequenceEvaluationService evaluation, IClock clock)
    : IRequestHandler<CompleteSequenceStepCommand, SequenceInstanceResponseModel>
{
    public async Task<SequenceInstanceResponseModel> Handle(CompleteSequenceStepCommand request, CancellationToken cancellationToken)
    {
        var i = await SequenceStepCommands.LoadRunning(db, request.InstanceId, cancellationToken);
        var step = SequenceStepCommands.Step(i, request.StepKey);
        if (step.Status is not (SequenceStepStatus.InProgress or SequenceStepStatus.Ready))
            throw new InvalidOperationException($"Step '{request.StepKey}' is {step.Status}; only Ready or InProgress steps can complete.");

        var now = clock.UtcNow;
        var def = i.Definition!.Steps.First(s => s.Key == step.StepKey);
        step.StartedAt ??= now;
        step.StartedByUserId ??= request.UserId;
        step.Status = SequenceStepStatus.Complete;
        step.CompletedAt = now;
        step.CompletedByUserId = request.UserId;
        db.SequenceEvents.Add(SequenceEvaluator.Event(i, SequenceEventType.StepCompleted, now, request.UserId, step.StepKey));
        db.LogActivityAt("sequence-step-completed", $"Sequence step '{def.Name}' completed", SequenceQueries.IndexingPoints(i));
        await db.SaveChangesAsync(cancellationToken);
        await evaluation.EvaluateAsync(i.Id, request.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(await db.SequenceInstances.WithGraph().FirstAsync(x => x.Id == i.Id, cancellationToken));
    }
}
