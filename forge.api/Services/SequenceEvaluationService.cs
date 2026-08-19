using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Sequences;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// The storage-aware half of the engine (the pure half is <see cref="SequenceEvaluator"/>). Resolves each gate's
/// <see cref="IGateSource"/> from DI — built-ins by <see cref="SequenceGateSourceType"/>, Custom by config key —
/// and fails closed (NoGo) when a source is missing, so a misconfigured gate can never silently open a step.
/// </summary>
public class SequenceEvaluationService(
    AppDbContext db,
    IEnumerable<IGateSource> gateSources,
    IClock clock,
    IPublisher publisher) : ISequenceEvaluationService
{
    public async Task<SequenceEvaluation> EvaluateAsync(int instanceId, int? actorUserId, CancellationToken cancellationToken)
    {
        var instance = await db.SequenceInstances
            .Include(i => i.Definition!).ThenInclude(d => d.Steps)
            .Include(i => i.Definition!).ThenInclude(d => d.Edges)
            .Include(i => i.Definition!).ThenInclude(d => d.Gates)
            .Include(i => i.Steps)
            .Include(i => i.Gates)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sequence instance {instanceId} not found.");

        if (instance.Status != SequenceInstanceStatus.Running)
            return new SequenceEvaluation();

        var now = clock.UtcNow;
        var net = new SequenceNet(instance.Definition!);
        var verdicts = new Dictionary<(string, string), SequenceGateVerdictResult>();
        foreach (var gate in instance.Gates)
        {
            var def = instance.Definition!.Gates.FirstOrDefault(g => g.StepKey == gate.StepKey && g.Key == gate.GateKey);
            if (def is null) continue;
            var source = Resolve(def);
            verdicts[(gate.StepKey, gate.GateKey)] = source is null
                ? SequenceGateVerdictResult.NoGo($"No gate source registered for {Describe(def)}")
                : await source.EvaluateAsync(new SequenceGateContext(instance.Definition!, def, instance, gate, now), cancellationToken);
        }

        var evaluation = SequenceEvaluator.Evaluate(net, instance, verdicts, now, actorUserId);
        if (evaluation.Events.Count > 0) db.SequenceEvents.AddRange(evaluation.Events);

        foreach (var key in evaluation.NewlyReady)
            await publisher.Publish(new SequenceStepReadyEvent(instance.Id, key, instance.SubjectEntityType, instance.SubjectEntityId), cancellationToken);
        if (evaluation.CompletedInstance)
            await publisher.Publish(new SequenceInstanceCompletedEvent(instance.Id, instance.DefinitionId, instance.SubjectEntityType, instance.SubjectEntityId), cancellationToken);

        return evaluation;
    }

    private IGateSource? Resolve(Core.Entities.SequenceGateDefinition def)
    {
        if (def.SourceType != SequenceGateSourceType.Custom)
            return gateSources.FirstOrDefault(s => s.SourceType == def.SourceType && s.CustomKey is null);
        var key = Features.Sequences.GateSources.SequenceGateConfig.Parse(def.ConfigJson).GetString("key");
        return string.IsNullOrEmpty(key) ? null
            : gateSources.FirstOrDefault(s => s.SourceType == SequenceGateSourceType.Custom && string.Equals(s.CustomKey, key, StringComparison.Ordinal));
    }

    private static string Describe(Core.Entities.SequenceGateDefinition def) =>
        def.SourceType == SequenceGateSourceType.Custom
            ? $"custom key '{Features.Sequences.GateSources.SequenceGateConfig.Parse(def.ConfigJson).GetString("key")}'"
            : def.SourceType.ToString();
}
