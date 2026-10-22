using FluentValidation;
using FluentValidation.Results;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Routing;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Writes a routing step's "must follow" links and keeps every link in the routing pointing at a lower step
/// number. A step's own links are validated against the rule and refused when they break it; links that a
/// renumber or reorder has broken are reported as a <see cref="PredecessorConflictException"/>, or removed when
/// the caller asked for that. Changes are logged on the part and left for the caller to save.
/// </summary>
public static class RoutingPredecessorEditor
{
    public const int MaxPredecessors = 50;

    public static async Task ApplyAsync(
        IPartRepository repo,
        int partId,
        Operation? operation,
        IReadOnlyList<OperationPredecessorRequestModel>? requested,
        bool removeConflicting,
        CancellationToken ct)
    {
        var routing = (await repo.GetRoutingAsync(partId, ct)).Where(o => o.DeletedAt == null).ToList();
        var links = await repo.GetPredecessorLinksAsync(partId, ct);
        var stepById = routing.ToDictionary(o => o.Id, o => o.StepNumber);

        if (operation is not null && requested is not null)
        {
            var wanted = requested.GroupBy(r => r.OperationId).Select(g => g.First()).ToList();
            var invalid = wanted.Where(r => !stepById.TryGetValue(r.OperationId, out var step)
                    || r.OperationId == operation.Id || step >= operation.StepNumber)
                .ToList();
            if (invalid.Count > 0)
                throw new ValidationException([
                    new ValidationFailure("predecessors",
                        $"Op {operation.StepNumber} can only follow steps with a lower step number in the same routing."),
                ]);

            var own = links.Where(l => l.OperationId == operation.Id).ToList();
            var before = Describe(own, stepById);
            foreach (var link in own.Where(l => wanted.All(w => w.OperationId != l.PredecessorOperationId)))
            {
                repo.RemovePredecessorLink(link);
                links.Remove(link);
            }
            foreach (var want in wanted)
            {
                var existing = own.FirstOrDefault(l => l.PredecessorOperationId == want.OperationId);
                if (existing is not null)
                {
                    existing.Gate = want.Gate;
                    continue;
                }
                var link = new OperationPredecessor
                {
                    Operation = operation,
                    OperationId = operation.Id,
                    PredecessorOperationId = want.OperationId,
                    Gate = want.Gate,
                };
                repo.AddPredecessorLink(link);
                links.Add(link);
            }

            var after = Describe(links.Where(l => l.OperationId == operation.Id).ToList(), stepById);
            if (before != after)
                repo.LogPartActivity(partId, "routing-predecessors-changed",
                    $"Op {operation.StepNumber} {operation.Title} now follows {after}");
        }

        ResolveConflicts(repo, partId, routing, links, removeConflicting);
    }

    /// <summary>
    /// Unlinks a step that is being deleted. A step that followed it follows its predecessors instead, with the
    /// same gate, so the order around the gap holds.
    /// </summary>
    public static async Task RemoveStepAsync(IPartRepository repo, int partId, Operation operation, CancellationToken ct)
    {
        var routing = (await repo.GetRoutingAsync(partId, ct)).Where(o => o.DeletedAt == null).ToList();
        var links = await repo.GetPredecessorLinksAsync(partId, ct);
        var effective = RoutingPredecessors.Effective(
            routing.Select(o => new RoutingStep(o.Id, o.StepNumber)).ToList(),
            links.Select(l => new RoutingLink(l.OperationId, l.PredecessorOperationId, l.Gate)).ToList());
        var bridge = effective.GetValueOrDefault(operation.Id, []);

        foreach (var dependent in links.Where(l => l.PredecessorOperationId == operation.Id).ToList())
        {
            foreach (var predecessor in bridge)
            {
                if (links.Any(l => l.OperationId == dependent.OperationId && l.PredecessorOperationId == predecessor.PredecessorOperationId))
                    continue;
                var link = new OperationPredecessor
                {
                    OperationId = dependent.OperationId,
                    PredecessorOperationId = predecessor.PredecessorOperationId,
                    Gate = dependent.Gate,
                };
                repo.AddPredecessorLink(link);
                links.Add(link);
            }
        }

        foreach (var link in links.Where(l => l.OperationId == operation.Id || l.PredecessorOperationId == operation.Id).ToList())
            repo.RemovePredecessorLink(link);
    }

    public static void ResolveConflicts(
        IPartRepository repo, int partId, IReadOnlyList<Operation> routing, List<OperationPredecessor> links, bool removeConflicting)
    {
        var steps = routing.Select(o => new RoutingStep(o.Id, o.StepNumber)).ToList();
        var stepById = routing.ToDictionary(o => o.Id, o => o.StepNumber);
        var conflicting = RoutingPredecessors.Conflicts(steps,
                links.Select(l => new RoutingLink(l.OperationId, l.PredecessorOperationId, l.Gate)).ToList())
            .Where(c => stepById.ContainsKey(c.OperationId) && stepById.ContainsKey(c.PredecessorOperationId))
            .ToList();
        if (conflicting.Count == 0)
            return;

        var conflicts = conflicting
            .Select(c => new PredecessorConflictResponseModel(
                c.OperationId, stepById[c.OperationId], c.PredecessorOperationId, stepById[c.PredecessorOperationId]))
            .OrderBy(c => c.StepNumber).ThenBy(c => c.PredecessorStepNumber)
            .ToList();
        if (!removeConflicting)
            throw new PredecessorConflictException(conflicts);

        foreach (var link in links.Where(l => conflicting.Any(c =>
                     c.OperationId == l.OperationId && c.PredecessorOperationId == l.PredecessorOperationId)).ToList())
        {
            repo.RemovePredecessorLink(link);
            links.Remove(link);
        }
        repo.LogPartActivity(partId, "routing-predecessors-removed",
            "Removed step links that no longer fit the step order: "
                + string.Join(", ", conflicts.Select(c => $"Op {c.StepNumber} after Op {c.PredecessorStepNumber}")));
    }

    private static string Describe(IReadOnlyList<OperationPredecessor> links, IReadOnlyDictionary<int, int> stepById)
        => links.Count == 0
            ? "the previous step"
            : string.Join(", ", links
                .Select(l => (Step: stepById.GetValueOrDefault(l.PredecessorOperationId), l.Gate))
                .OrderBy(l => l.Step)
                .Select(l => l.Gate == PredecessorGate.Complete ? $"Op {l.Step} (done)" : $"Op {l.Step}"));
}
