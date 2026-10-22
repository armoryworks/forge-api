using Forge.Core.Enums;

namespace Forge.Core.Routing;

/// <summary>A "must follow" link: <paramref name="OperationId"/> follows <paramref name="PredecessorOperationId"/>.</summary>
public sealed record RoutingLink(int OperationId, int PredecessorOperationId, PredecessorGate Gate);
