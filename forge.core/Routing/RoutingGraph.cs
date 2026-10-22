using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Forge.Core.Enums;

namespace Forge.Core.Routing;

/// <summary>
/// Checks a whole routing graph sent in one save, and fingerprints a routing so a save can tell that someone else
/// changed it since it was loaded. The rules are the ones <see cref="RoutingPredecessors"/> relies on: every live
/// step is sent exactly once with a positive step number, and every link points at another step of the same
/// routing whose new step number is lower, so the saved graph can't have a cycle.
/// </summary>
public static class RoutingGraph
{
    public static IReadOnlyList<RoutingGraphIssue> Validate(
        IReadOnlyList<int> routingOperationIds, IReadOnlyList<RoutingGraphStep> requested, int maxPredecessors)
    {
        var issues = new List<RoutingGraphIssue>();
        var live = routingOperationIds.ToHashSet();
        var sent = new HashSet<int>();
        var stepById = new Dictionary<int, int>();

        foreach (var step in requested)
        {
            if (!live.Contains(step.OperationId))
            {
                issues.Add(new RoutingGraphIssue(RoutingGraphIssueKind.UnknownStep, step.OperationId, step.StepNumber));
                continue;
            }
            if (!sent.Add(step.OperationId))
            {
                issues.Add(new RoutingGraphIssue(RoutingGraphIssueKind.DuplicateStep, step.OperationId, step.StepNumber));
                continue;
            }
            if (step.StepNumber <= 0)
                issues.Add(new RoutingGraphIssue(RoutingGraphIssueKind.InvalidStepNumber, step.OperationId, step.StepNumber));
            stepById[step.OperationId] = step.StepNumber;
        }

        issues.AddRange(routingOperationIds
            .Where(id => !sent.Contains(id))
            .Distinct()
            .Select(id => new RoutingGraphIssue(RoutingGraphIssueKind.MissingStep, id, null)));

        foreach (var step in requested.Where(s => stepById.ContainsKey(s.OperationId)).DistinctBy(s => s.OperationId))
        {
            var links = step.Predecessors.DistinctBy(l => l.PredecessorOperationId).ToList();
            if (links.Count > maxPredecessors)
                issues.Add(new RoutingGraphIssue(RoutingGraphIssueKind.TooManyPredecessors, step.OperationId, step.StepNumber));

            foreach (var link in links)
            {
                if (link.PredecessorOperationId == step.OperationId)
                {
                    issues.Add(new RoutingGraphIssue(RoutingGraphIssueKind.SelfLink,
                        step.OperationId, step.StepNumber, link.PredecessorOperationId, step.StepNumber));
                    continue;
                }
                if (!stepById.TryGetValue(link.PredecessorOperationId, out var predecessorStep))
                {
                    issues.Add(new RoutingGraphIssue(RoutingGraphIssueKind.UnknownPredecessor,
                        step.OperationId, step.StepNumber, link.PredecessorOperationId));
                    continue;
                }
                if (predecessorStep >= step.StepNumber)
                    issues.Add(new RoutingGraphIssue(RoutingGraphIssueKind.PredecessorNotEarlier,
                        step.OperationId, step.StepNumber, link.PredecessorOperationId, predecessorStep));
            }
        }

        return issues;
    }

    /// <summary>
    /// A short fingerprint of the routing's step numbers and explicit links. It changes whenever a step is added,
    /// removed or renumbered, or a link or its gate changes, and does not depend on the order of the inputs.
    /// </summary>
    public static string Version(IReadOnlyList<RoutingStep> steps, IReadOnlyList<RoutingLink> links)
    {
        var text = new StringBuilder();
        foreach (var step in steps.OrderBy(s => s.OperationId))
            text.Append(CultureInfo.InvariantCulture, $"{step.OperationId}:{step.StepNumber};");
        text.Append('|');
        foreach (var link in links.OrderBy(l => l.OperationId).ThenBy(l => l.PredecessorOperationId))
            text.Append(CultureInfo.InvariantCulture, $"{link.OperationId}>{link.PredecessorOperationId}:{(short)link.Gate};");

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }
}
