using Forge.Core.Entities;

namespace Forge.Core.Sequences;

/// <summary>
/// Immutable graph view of one <see cref="SequenceDefinition"/> — steps by key, predecessor/successor adjacency,
/// gates by step. Built once per evaluation; validated by <see cref="SequenceNetValidator"/>.
/// </summary>
public sealed class SequenceNet
{
    private readonly Dictionary<string, SequenceStepDefinition> _steps;
    private readonly Dictionary<string, List<SequenceEdgeDefinition>> _incoming;
    private readonly Dictionary<string, List<SequenceEdgeDefinition>> _outgoing;
    private readonly Dictionary<string, List<SequenceGateDefinition>> _gates;

    public SequenceNet(SequenceDefinition definition)
    {
        Definition = definition;
        _steps = definition.Steps.ToDictionary(s => s.Key, StringComparer.Ordinal);
        _incoming = definition.Steps.ToDictionary(s => s.Key, _ => new List<SequenceEdgeDefinition>(), StringComparer.Ordinal);
        _outgoing = definition.Steps.ToDictionary(s => s.Key, _ => new List<SequenceEdgeDefinition>(), StringComparer.Ordinal);
        _gates = definition.Steps.ToDictionary(s => s.Key, _ => new List<SequenceGateDefinition>(), StringComparer.Ordinal);
        foreach (var e in definition.Edges)
        {
            if (_incoming.TryGetValue(e.ToStepKey, out var inc)) inc.Add(e);
            if (_outgoing.TryGetValue(e.FromStepKey, out var outg)) outg.Add(e);
        }
        foreach (var g in definition.Gates)
        {
            if (_gates.TryGetValue(g.StepKey, out var list)) list.Add(g);
        }
    }

    public SequenceDefinition Definition { get; }

    public IReadOnlyCollection<SequenceStepDefinition> Steps => _steps.Values;

    public bool TryGetStep(string key, out SequenceStepDefinition step) => _steps.TryGetValue(key, out step!);

    /// <summary>Non-rework predecessors — the ones that gate readiness.</summary>
    public IEnumerable<string> PredecessorsOf(string stepKey) =>
        _incoming.TryGetValue(stepKey, out var l) ? l.Where(e => !e.IsRework).Select(e => e.FromStepKey) : [];

    public IEnumerable<string> SuccessorsOf(string stepKey) =>
        _outgoing.TryGetValue(stepKey, out var l) ? l.Where(e => !e.IsRework).Select(e => e.ToStepKey) : [];

    public IReadOnlyList<SequenceGateDefinition> GatesOf(string stepKey) =>
        _gates.TryGetValue(stepKey, out var l) ? l : [];

    /// <summary>Steps with no non-rework predecessors — where a run begins.</summary>
    public IEnumerable<SequenceStepDefinition> StartSteps() =>
        _steps.Values.Where(s => !PredecessorsOf(s.Key).Any());

    /// <summary>Every step reachable downstream of <paramref name="stepKey"/> via non-rework edges (excluding itself).</summary>
    public HashSet<string> Downstream(string stepKey)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(SuccessorsOf(stepKey));
        while (stack.Count > 0)
        {
            var k = stack.Pop();
            if (!seen.Add(k)) continue;
            foreach (var s in SuccessorsOf(k)) stack.Push(s);
        }
        return seen;
    }
}
