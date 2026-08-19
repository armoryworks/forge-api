using Forge.Core.Sequences;

namespace Forge.Core.Interfaces;

/// <summary>
/// Runs one evaluation pass over a sequence instance: loads it with its definition, asks every gate's source for a
/// verdict, applies <see cref="SequenceEvaluator"/>, appends the resulting events, and publishes domain events.
/// Callers save changes themselves (it participates in the caller's unit of work).
/// </summary>
public interface ISequenceEvaluationService
{
    Task<SequenceEvaluation> EvaluateAsync(int instanceId, int? actorUserId, CancellationToken cancellationToken);
}
