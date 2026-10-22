namespace Forge.Core.Routing;

/// <summary>
/// Pieces a job step may report under step gating. <paramref name="Available"/> is null when the step has no
/// predecessors; <paramref name="LimitingOperationId"/> is the predecessor that sets the limit.
/// </summary>
public sealed record StepAvailability(decimal? Available, int? LimitingOperationId);
