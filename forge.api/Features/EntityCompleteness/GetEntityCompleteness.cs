using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Workflows;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EntityCompleteness;

/// <summary>
/// Compute the per-capability completeness breakdown for one entity.
/// Filtered to only include capabilities currently enabled on this install
/// (per <see cref="ICapabilitySnapshotProvider"/>) — disabled-capability
/// requirements don't trigger false "incomplete" alarms.
///
/// Per-EntityType loaders are switched here directly (Vendor / Part /
/// Customer for the initial three surfaces). Add a case for new entity
/// types as the chip wiring expands. Predicate evaluation reuses the
/// shared <see cref="PredicateEvaluator"/> from the workflow substrate
/// — same JSON DSL.
/// </summary>
public record GetEntityCompletenessQuery(string EntityType, int EntityId)
    : IRequest<EntityCompletenessResponseModel>;

public class GetEntityCompletenessHandler(
    AppDbContext db,
    ICapabilitySnapshotProvider snapshots,
    PredicateEvaluator evaluator)
    : IRequestHandler<GetEntityCompletenessQuery, EntityCompletenessResponseModel>
{
    public async Task<EntityCompletenessResponseModel> Handle(
        GetEntityCompletenessQuery request,
        CancellationToken cancellationToken)
    {
        var entity = await LoadEntityAsync(request.EntityType, request.EntityId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Entity not found: {request.EntityType}#{request.EntityId}");

        var requirements = await db.EntityCapabilityRequirements.AsNoTracking()
            .Where(r => r.EntityType == request.EntityType)
            .OrderBy(r => r.CapabilityCode)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.RequirementId)
            .ToListAsync(cancellationToken);

        // Filter out requirements whose capability is currently disabled —
        // those are not blocking on this install. Group what's left by
        // capability so the chip popover can show one section per cap.
        var snapshot = snapshots.Current;
        var enabledRequirements = requirements
            .Where(r => snapshot.IsEnabled(r.CapabilityCode))
            .GroupBy(r => r.CapabilityCode)
            .ToList();

        var byCode = CapabilityCatalog.All.ToDictionary(c => c.Code, c => c.Name);
        var capabilities = new List<EntityCompletenessCapability>(enabledRequirements.Count);

        foreach (var group in enabledRequirements)
        {
            var missing = new List<EntityCompletenessMissingField>();
            foreach (var requirement in group.Where(requirement => !evaluator.Evaluate(requirement.Predicate, entity)))
            {
                missing.Add(new EntityCompletenessMissingField(
                    requirement.RequirementId,
                    requirement.DisplayNameKey,
                    requirement.MissingMessageKey));
            }
            capabilities.Add(new EntityCompletenessCapability(
                group.Key,
                byCode.GetValueOrDefault(group.Key, group.Key),
                missing.Count == 0,
                missing));
        }

        return new EntityCompletenessResponseModel(
            request.EntityType,
            request.EntityId,
            capabilities);
    }

    /// <summary>
    /// Per-EntityType loader switch. Each branch loads the entity with
    /// AsNoTracking + just the columns the predicate evaluator needs
    /// (currently scalar fields — no Include() since unfilled navigation
    /// collections naturally evaluate as "missing", which is the right
    /// chip answer for stub entities).
    /// </summary>
    private async Task<object?> LoadEntityAsync(string entityType, int id, CancellationToken ct)
    {
        return entityType switch
        {
            "Vendor" => await db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct),
            "Part" => await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct),
            "Customer" => await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct),
            _ => null,
        };
    }
}
