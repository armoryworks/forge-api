namespace Forge.Core.Routing;

/// <summary>A step of a requested routing graph: its new step number and the links it must follow.</summary>
public sealed record RoutingGraphStep(int OperationId, int StepNumber, IReadOnlyList<RoutingLink> Predecessors);
