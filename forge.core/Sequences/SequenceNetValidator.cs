using Forge.Core.Entities;

namespace Forge.Core.Sequences;

/// <summary>
/// Structural rules a definition must satisfy before it can be published: unique step keys, edges between known
/// steps, gates on known steps with unique keys per step, at least one start step, every step reachable from a
/// start, and no cycles except through edges flagged IsRework.
/// </summary>
public static class SequenceNetValidator
{
    public static IReadOnlyList<string> Validate(SequenceDefinition definition)
    {
        var errors = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in definition.Steps)
        {
            if (string.IsNullOrWhiteSpace(s.Key)) errors.Add("A step has an empty key.");
            else if (!keys.Add(s.Key)) errors.Add($"Duplicate step key '{s.Key}'.");
        }
        if (definition.Steps.Count == 0) errors.Add("A definition needs at least one step.");

        foreach (var e in definition.Edges)
        {
            if (!keys.Contains(e.FromStepKey)) errors.Add($"Edge from unknown step '{e.FromStepKey}'.");
            if (!keys.Contains(e.ToStepKey)) errors.Add($"Edge to unknown step '{e.ToStepKey}'.");
            if (e.FromStepKey == e.ToStepKey) errors.Add($"Step '{e.FromStepKey}' cannot depend on itself.");
        }

        var gateKeys = new HashSet<(string, string)>();
        foreach (var g in definition.Gates)
        {
            if (!keys.Contains(g.StepKey)) errors.Add($"Gate '{g.Key}' is on unknown step '{g.StepKey}'.");
            if (string.IsNullOrWhiteSpace(g.Key)) errors.Add($"A gate on step '{g.StepKey}' has an empty key.");
            else if (!gateKeys.Add((g.StepKey, g.Key))) errors.Add($"Duplicate gate key '{g.Key}' on step '{g.StepKey}'.");
        }

        if (errors.Count > 0) return errors; // graph checks need a well-formed key set

        var net = new SequenceNet(definition);
        var starts = net.StartSteps().Select(s => s.Key).ToList();
        if (starts.Count == 0) errors.Add("No start step: every step has a predecessor (a cycle not marked as rework).");

        // Reachability from the start steps over non-rework edges.
        var reachable = new HashSet<string>(starts, StringComparer.Ordinal);
        foreach (var s in starts) reachable.UnionWith(net.Downstream(s));
        foreach (var k in keys.Where(k => !reachable.Contains(k)))
            errors.Add($"Step '{k}' is unreachable from any start step.");

        // Cycle detection over non-rework edges (DFS colouring).
        var colour = keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        foreach (var k in keys)
        {
            if (colour[k] == 0 && HasCycle(net, k, colour)) { errors.Add("The dependency graph has a cycle that is not marked as rework."); break; }
        }
        return errors;
    }

    private static bool HasCycle(SequenceNet net, string key, Dictionary<string, int> colour)
    {
        colour[key] = 1;
        foreach (var next in net.SuccessorsOf(key))
        {
            if (colour[next] == 1) return true;
            if (colour[next] == 0 && HasCycle(net, next, colour)) return true;
        }
        colour[key] = 2;
        return false;
    }
}
