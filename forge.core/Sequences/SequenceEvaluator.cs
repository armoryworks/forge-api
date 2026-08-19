using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Core.Sequences;

/// <summary>
/// The pure marking evaluator. Given the net, the run's step/gate instances, and fresh gate verdicts, it applies
/// the verdicts and derives step readiness, appending one <see cref="SequenceEvent"/> per change. Deterministic and
/// idempotent: running it twice with the same inputs changes nothing the second time. It never talks to storage,
/// clocks, or gate sources — callers gather verdicts first (see the api-side <c>SequenceEvaluationService</c>).
///
/// Rules:
/// <list type="number">
/// <item>An overridden gate stays Go regardless of its source's verdict.</item>
/// <item>A step is "predecessor-satisfied" when its join policy holds over predecessors in Complete/Skipped.</item>
/// <item>Pending → Ready when predecessor-satisfied and every gate is Go. Ready → Pending when a gate stops being Go
/// (the step has not started, so nothing is lost). Blocked = predecessor-satisfied ∧ ¬all-gates-Go (derived).</item>
/// <item>The instance completes when every step is Complete or Skipped.</item>
/// </list>
/// </summary>
public static class SequenceEvaluator
{
    public static SequenceEvaluation Evaluate(
        SequenceNet net,
        SequenceInstance instance,
        IReadOnlyDictionary<(string StepKey, string GateKey), SequenceGateVerdictResult> verdicts,
        DateTimeOffset now,
        int? actorUserId = null)
    {
        var result = new SequenceEvaluation();
        var steps = instance.Steps.ToDictionary(s => s.StepKey, StringComparer.Ordinal);
        var gates = instance.Gates.ToDictionary(g => (g.StepKey, g.GateKey));

        // 1. apply verdicts
        foreach (var gate in instance.Gates)
        {
            if (!verdicts.TryGetValue((gate.StepKey, gate.GateKey), out var v)) continue;
            var effective = gate.OverriddenAt.HasValue ? SequenceGateVerdict.Go : v.Verdict;
            var reason = gate.OverriddenAt.HasValue ? $"Overridden: {gate.OverrideReason}" : v.Reason;
            gate.LastEvaluatedAt = now;
            if (gate.Verdict == effective && gate.Reason == reason) continue;
            gate.Verdict = effective;
            gate.Reason = reason;
            result.Events.Add(Event(instance, SequenceEventType.GateEvaluated, now, actorUserId, gate.StepKey, gate.GateKey,
                $"{{\"verdict\":\"{effective}\",\"reason\":{Json(reason)}}}"));
        }

        // 2. derive readiness (iterate to a fixed point — Skipped/Complete are only changed by commands, so one
        //    pass suffices for readiness, but a loop keeps this correct if that ever changes)
        bool changed;
        do
        {
            changed = false;
            foreach (var stepDef in net.Steps)
            {
                if (!steps.TryGetValue(stepDef.Key, out var step)) continue;
                if (step.Status is SequenceStepStatus.InProgress or SequenceStepStatus.Complete or SequenceStepStatus.Skipped) continue;

                var predsOk = PredecessorsSatisfied(net, stepDef, steps);
                var gatesOk = net.GatesOf(stepDef.Key).All(g =>
                    gates.TryGetValue((stepDef.Key, g.Key), out var gi) && gi.Verdict == SequenceGateVerdict.Go);

                if (predsOk && gatesOk && step.Status == SequenceStepStatus.Pending)
                {
                    step.Status = SequenceStepStatus.Ready;
                    step.ReadyAt = now;
                    result.NewlyReady.Add(step.StepKey);
                    result.Events.Add(Event(instance, SequenceEventType.StepReady, now, actorUserId, step.StepKey));
                    changed = true;
                }
                else if (!(predsOk && gatesOk) && step.Status == SequenceStepStatus.Ready)
                {
                    step.Status = SequenceStepStatus.Pending;
                    step.ReadyAt = null;
                    result.Events.Add(Event(instance, SequenceEventType.StepBlocked, now, actorUserId, step.StepKey,
                        $"{{\"reason\":{Json(BlockedReason(net, stepDef, gates))}}}"));
                    changed = true;
                }

                if (predsOk && !gatesOk) result.Blocked.Add(step.StepKey);
            }
        } while (changed);

        // 3. completion
        if (instance.Status == SequenceInstanceStatus.Running &&
            steps.Values.All(s => s.Status is SequenceStepStatus.Complete or SequenceStepStatus.Skipped))
        {
            instance.Status = SequenceInstanceStatus.Completed;
            instance.CompletedAt = now;
            result.CompletedInstance = true;
            result.Events.Add(Event(instance, SequenceEventType.InstanceCompleted, now, actorUserId));
        }

        return result;
    }

    /// <summary>Derived: predecessors satisfied but at least one gate is not Go.</summary>
    public static bool IsBlocked(SequenceNet net, SequenceInstance instance, string stepKey)
    {
        var steps = instance.Steps.ToDictionary(s => s.StepKey, StringComparer.Ordinal);
        if (!net.TryGetStep(stepKey, out var def) || !steps.TryGetValue(stepKey, out var step)) return false;
        if (step.Status is not SequenceStepStatus.Pending) return false;
        var gates = instance.Gates.ToDictionary(g => (g.StepKey, g.GateKey));
        return PredecessorsSatisfied(net, def, steps) &&
               !net.GatesOf(stepKey).All(g => gates.TryGetValue((stepKey, g.Key), out var gi) && gi.Verdict == SequenceGateVerdict.Go);
    }

    public static bool PredecessorsSatisfied(SequenceNet net, SequenceStepDefinition step, IReadOnlyDictionary<string, SequenceStepInstance> steps)
    {
        var preds = net.PredecessorsOf(step.Key).ToList();
        if (preds.Count == 0) return true;
        bool Done(string k) => steps.TryGetValue(k, out var p) && p.Status is SequenceStepStatus.Complete or SequenceStepStatus.Skipped;
        return step.JoinPolicy == SequenceJoinPolicy.Any ? preds.Any(Done) : preds.All(Done);
    }

    public static string BlockedReason(SequenceNet net, SequenceStepDefinition step, IReadOnlyDictionary<(string, string), SequenceGateInstance> gates)
    {
        var parts = net.GatesOf(step.Key)
            .Where(g => !(gates.TryGetValue((step.Key, g.Key), out var gi) && gi.Verdict == SequenceGateVerdict.Go))
            .Select(g => gates.TryGetValue((step.Key, g.Key), out var gi) && !string.IsNullOrEmpty(gi.Reason) ? $"{g.Name}: {gi.Reason}" : $"{g.Name}: not go");
        return string.Join("; ", parts);
    }

    public static SequenceEvent Event(SequenceInstance instance, SequenceEventType type, DateTimeOffset now, int? actor,
        string? stepKey = null, string? gateKey = null, string? payloadJson = null) => new()
    {
        InstanceId = instance.Id,
        Instance = instance,
        Type = type,
        StepKey = stepKey,
        GateKey = gateKey,
        PayloadJson = payloadJson,
        OccurredAt = now,
        ActorUserId = actor,
    };

    private static string Json(string? s) => s is null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
