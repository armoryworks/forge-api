namespace Forge.Core.Routing;

/// <summary>One live step of a routing, as <see cref="RoutingPredecessors"/> sees it.</summary>
public sealed record RoutingStep(int OperationId, int StepNumber);
