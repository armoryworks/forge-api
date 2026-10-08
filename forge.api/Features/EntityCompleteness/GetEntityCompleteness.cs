using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Models;

namespace Forge.Api.Features.EntityCompleteness;

/// <summary>
/// Compute the per-capability completeness breakdown for one entity.
/// Filtered to only include capabilities currently enabled on this install
/// (per <see cref="ICapabilitySnapshotProvider"/>) — disabled-capability
/// requirements don't trigger false "incomplete" alarms. Evaluation is
/// shared with the batch lookup through
/// <see cref="EntityCompletenessEvaluator"/>.
/// </summary>
public record GetEntityCompletenessQuery(string EntityType, int EntityId)
    : IRequest<EntityCompletenessResponseModel>;

public class GetEntityCompletenessHandler(EntityCompletenessEvaluator evaluator)
    : IRequestHandler<GetEntityCompletenessQuery, EntityCompletenessResponseModel>
{
    public async Task<EntityCompletenessResponseModel> Handle(
        GetEntityCompletenessQuery request,
        CancellationToken cancellationToken)
    {
        var results = await evaluator.EvaluateAsync(request.EntityType, [request.EntityId], cancellationToken);
        return results.Count > 0
            ? results[0]
            : throw new KeyNotFoundException(
                $"Entity not found: {request.EntityType}#{request.EntityId}");
    }
}
