using Forge.Core.Models;
using Forge.Core.Routing;

namespace Forge.Api.Features.Parts;

/// <summary>The <see cref="RoutingGraph.Version"/> of a routing as the operations list returns it.</summary>
public static class RoutingGraphVersion
{
    public static string Of(IReadOnlyList<OperationResponseModel> operations)
        => RoutingGraph.Version(
            operations.Select(o => new RoutingStep(o.Id, o.StepNumber)).ToList(),
            operations.SelectMany(o => (o.Predecessors ?? []).Select(p => new RoutingLink(o.Id, p.OperationId, p.Gate))).ToList());
}
