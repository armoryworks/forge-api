using Forge.Core.Enums;

namespace Forge.Core.Routing;

/// <summary>
/// Resolves the predecessors each routing step actually follows. Explicit links win; a step with none (or whose
/// links all point outside the routing) follows the step or steps with the next lower step number, with a
/// Quantity gate. The first step follows nothing. A link is valid only when both ends are in the routing and the
/// predecessor's step number is lower, so the graph can't have a cycle.
/// </summary>
public static class RoutingPredecessors
{
    public static IReadOnlyDictionary<int, IReadOnlyList<RoutingLink>> Effective(
        IReadOnlyList<RoutingStep> steps, IReadOnlyList<RoutingLink> links)
    {
        var stepById = steps.GroupBy(s => s.OperationId).ToDictionary(g => g.Key, g => g.First().StepNumber);
        var explicitLinks = links
            .Where(l => IsValid(l, stepById))
            .GroupBy(l => l.OperationId)
            .ToDictionary(g => g.Key, g => g.GroupBy(l => l.PredecessorOperationId).Select(d => d.First()).ToList());

        var result = new Dictionary<int, IReadOnlyList<RoutingLink>>();
        foreach (var step in steps)
        {
            if (explicitLinks.TryGetValue(step.OperationId, out var own) && own.Count > 0)
            {
                result[step.OperationId] = own;
                continue;
            }

            var lower = steps.Where(s => s.StepNumber < step.StepNumber).ToList();
            if (lower.Count == 0)
            {
                result[step.OperationId] = [];
                continue;
            }

            var previous = lower.Max(s => s.StepNumber);
            result[step.OperationId] = lower
                .Where(s => s.StepNumber == previous)
                .Select(s => new RoutingLink(step.OperationId, s.OperationId, PredecessorGate.Quantity))
                .ToList();
        }

        return result;
    }

    /// <summary>
    /// The links that break the rules: self-links, links to a step outside the routing, and links whose
    /// predecessor does not have a lower step number.
    /// </summary>
    public static IReadOnlyList<RoutingLink> Conflicts(IReadOnlyList<RoutingStep> steps, IReadOnlyList<RoutingLink> links)
    {
        var stepById = steps.GroupBy(s => s.OperationId).ToDictionary(g => g.Key, g => g.First().StepNumber);
        return links.Where(l => !IsValid(l, stepById)).ToList();
    }

    private static bool IsValid(RoutingLink link, IReadOnlyDictionary<int, int> stepById)
        => link.OperationId != link.PredecessorOperationId
            && stepById.TryGetValue(link.OperationId, out var step)
            && stepById.TryGetValue(link.PredecessorOperationId, out var predecessorStep)
            && predecessorStep < step;
}
