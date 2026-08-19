using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Sequences;

namespace Forge.Tests.Sequences;

/// <summary>Builders for the small nets the engine tests use.</summary>
public static class SequenceTestNets
{
    public static SequenceDefinition Definition(string code, params string[] steps)
    {
        var d = new SequenceDefinition { Id = 1, Code = code, Version = 1, Name = code, Status = SequenceDefinitionStatus.Published };
        var i = 0;
        foreach (var s in steps) d.Steps.Add(new SequenceStepDefinition { Key = s, Name = s.ToUpperInvariant(), SortOrder = i++ });
        return d;
    }

    public static SequenceDefinition Edge(this SequenceDefinition d, string from, string to, bool rework = false)
    {
        d.Edges.Add(new SequenceEdgeDefinition { FromStepKey = from, ToStepKey = to, IsRework = rework });
        return d;
    }

    public static SequenceDefinition Gate(this SequenceDefinition d, string step, string key, SequenceGateSourceType type = SequenceGateSourceType.ManualClearance, string config = "{}")
    {
        d.Gates.Add(new SequenceGateDefinition { StepKey = step, Key = key, Name = key, SourceType = type, ConfigJson = config });
        return d;
    }

    /// <summary>A serial a → b → c with a manual gate on b.</summary>
    public static SequenceDefinition Serial() => Definition("serial", "a", "b", "c").Edge("a", "b").Edge("b", "c").Gate("b", "inspect");

    /// <summary>Fork/join: prep1, prep2 → assemble → ship.</summary>
    public static SequenceDefinition ForkJoin() =>
        Definition("forkjoin", "prep1", "prep2", "assemble", "ship").Edge("prep1", "assemble").Edge("prep2", "assemble").Edge("assemble", "ship");

    public static SequenceInstance Instance(SequenceDefinition d, int id = 1)
    {
        var i = new SequenceInstance { Id = id, DefinitionId = d.Id, Definition = d, Status = SequenceInstanceStatus.Running, StartedAt = DateTimeOffset.UnixEpoch };
        foreach (var s in d.Steps) i.Steps.Add(new SequenceStepInstance { InstanceId = id, StepKey = s.Key });
        foreach (var g in d.Gates) i.Gates.Add(new SequenceGateInstance { InstanceId = id, StepKey = g.StepKey, GateKey = g.Key });
        return i;
    }

    public static Dictionary<(string, string), SequenceGateVerdictResult> Verdicts(params ((string, string) Gate, SequenceGateVerdictResult V)[] items) =>
        items.ToDictionary(x => x.Gate, x => x.V);

    public static SequenceEvaluation Eval(SequenceDefinition d, SequenceInstance i, Dictionary<(string, string), SequenceGateVerdictResult>? verdicts = null, DateTimeOffset? now = null) =>
        SequenceEvaluator.Evaluate(new SequenceNet(d), i, verdicts ?? new(), now ?? DateTimeOffset.UnixEpoch);

    public static SequenceStepInstance Step(this SequenceInstance i, string key) => i.Steps.First(s => s.StepKey == key);
    public static SequenceGateInstance Gate(this SequenceInstance i, string step, string key) => i.Gates.First(g => g.StepKey == step && g.GateKey == key);
}
