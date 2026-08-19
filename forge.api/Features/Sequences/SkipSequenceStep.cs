using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Sequences;

/// <summary>Skip a not-yet-complete step with a required reason; it counts as complete for successors.</summary>
public record SkipSequenceStepCommand(int InstanceId, string StepKey, string Reason, int UserId) : IRequest<SequenceInstanceResponseModel>;

public class SkipSequenceStepHandler(AppDbContext db, ISequenceEvaluationService evaluation, IClock clock)
    : IRequestHandler<SkipSequenceStepCommand, SequenceInstanceResponseModel>
{
    public async Task<SequenceInstanceResponseModel> Handle(SkipSequenceStepCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("A skip reason is required.");
        var i = await SequenceStepCommands.LoadRunning(db, request.InstanceId, cancellationToken);
        var step = SequenceStepCommands.Step(i, request.StepKey);
        if (step.Status is SequenceStepStatus.Complete or SequenceStepStatus.Skipped)
            throw new InvalidOperationException($"Step '{request.StepKey}' is already {step.Status}.");

        var now = clock.UtcNow;
        var def = i.Definition!.Steps.First(s => s.Key == step.StepKey);
        step.Status = SequenceStepStatus.Skipped;
        step.SkipReason = request.Reason.Trim();
        step.CompletedAt = now;
        step.CompletedByUserId = request.UserId;
        db.SequenceEvents.Add(SequenceEvaluator.Event(i, SequenceEventType.StepSkipped, now, request.UserId, step.StepKey,
            payloadJson: $"{{\"reason\":\"{step.SkipReason.Replace("\"", "\\\"")}\"}}"));
        db.LogActivityAt("sequence-step-skipped", $"Sequence step '{def.Name}' skipped: {step.SkipReason}", SequenceQueries.IndexingPoints(i));
        await db.SaveChangesAsync(cancellationToken);
        await evaluation.EvaluateAsync(i.Id, request.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return SequenceMapping.ToModel(await db.SequenceInstances.WithGraph().FirstAsync(x => x.Id == i.Id, cancellationToken));
    }
}
