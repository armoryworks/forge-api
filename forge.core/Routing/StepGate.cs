using Forge.Core.Enums;

namespace Forge.Core.Routing;

/// <summary>
/// The step-gating rules. Pieces available to a step are the fewest good pieces completed across its
/// predecessors: a Skipped predecessor passes its own predecessors through, and a Complete predecessor with no
/// count counts as the job quantity. A step's Complete-gated predecessors must be Complete or Skipped before the
/// step can be marked Done.
/// </summary>
public static class StepGate
{
    public static StepAvailability Available(
        int operationId,
        IReadOnlyDictionary<int, IReadOnlyList<RoutingLink>> predecessors,
        IReadOnlyDictionary<int, StepProgress> progress,
        decimal jobQuantity)
        => AvailableFrom(operationId, predecessors, progress, jobQuantity, []);

    public static IReadOnlyList<int> WaitingOn(
        int operationId,
        IReadOnlyDictionary<int, IReadOnlyList<RoutingLink>> predecessors,
        IReadOnlyDictionary<int, StepProgress> progress)
        => predecessors.GetValueOrDefault(operationId, [])
            .Where(l => l.Gate == PredecessorGate.Complete && !IsClosed(StatusOf(l.PredecessorOperationId, progress)))
            .Select(l => l.PredecessorOperationId)
            .ToList();

    private static StepAvailability AvailableFrom(
        int operationId,
        IReadOnlyDictionary<int, IReadOnlyList<RoutingLink>> predecessors,
        IReadOnlyDictionary<int, StepProgress> progress,
        decimal jobQuantity,
        HashSet<int> visiting)
    {
        var links = predecessors.GetValueOrDefault(operationId, []);
        if (links.Count == 0 || !visiting.Add(operationId))
            return new StepAvailability(null, null);

        decimal? best = null;
        int? limiting = null;
        foreach (var link in links)
        {
            var id = link.PredecessorOperationId;
            var state = progress.GetValueOrDefault(id);
            StepAvailability candidate = state?.Status switch
            {
                JobOperationStatus.Skipped => AvailableFrom(id, predecessors, progress, jobQuantity, visiting),
                JobOperationStatus.Complete when state.Completed <= 0m => new StepAvailability(jobQuantity, id),
                _ => new StepAvailability(state?.Completed ?? 0m, id),
            };
            var value = candidate.Available ?? jobQuantity;
            if (best is null || value < best)
            {
                best = value;
                limiting = candidate.LimitingOperationId ?? id;
            }
        }

        visiting.Remove(operationId);
        return new StepAvailability(best, limiting);
    }

    private static JobOperationStatus StatusOf(int operationId, IReadOnlyDictionary<int, StepProgress> progress)
        => progress.GetValueOrDefault(operationId)?.Status ?? JobOperationStatus.NotStarted;

    private static bool IsClosed(JobOperationStatus status)
        => status is JobOperationStatus.Complete or JobOperationStatus.Skipped;
}
