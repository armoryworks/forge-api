using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Workflows;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EntityCompleteness;

/// <summary>
/// Shared completeness evaluation behind the single-entity and batch
/// completeness endpoints, so the detail chip and the list badges always give
/// the same answer. Requirements whose capability is disabled on this install
/// are dropped (they are not blocking here); the rest are grouped by
/// capability so the chip popover can show one section per capability.
///
/// Per-EntityType loaders are switched here directly (Vendor / Part /
/// Customer for the initial three surfaces). Add a case for new entity
/// types as the chip wiring expands. Predicate evaluation reuses the
/// shared <see cref="PredicateEvaluator"/> from the workflow substrate
/// — same JSON DSL.
/// </summary>
public class EntityCompletenessEvaluator(
    AppDbContext db,
    ICapabilitySnapshotProvider snapshots,
    PredicateEvaluator evaluator)
{
    /// <summary>
    /// Evaluates every id in <paramref name="ids"/> and returns one entry per
    /// id that resolves to an entity, in the given order. Ids that do not
    /// resolve, and unknown entity types, produce no entry.
    /// </summary>
    public async Task<IReadOnlyList<EntityCompletenessResponseModel>> EvaluateAsync(
        string entityType,
        IReadOnlyList<int> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        var entities = await LoadEntitiesAsync(entityType, ids, ct);
        if (entities.Count == 0)
            return [];

        var requirements = await db.EntityCapabilityRequirements.AsNoTracking()
            .Where(r => r.EntityType == entityType)
            .OrderBy(r => r.CapabilityCode)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.RequirementId)
            .ToListAsync(ct);

        var snapshot = snapshots.Current;
        var enabledRequirements = requirements
            .Where(r => snapshot.IsEnabled(r.CapabilityCode))
            .GroupBy(r => r.CapabilityCode)
            .ToList();

        var byCode = CapabilityCatalog.All.ToDictionary(c => c.Code, c => c.Name);
        var results = new List<EntityCompletenessResponseModel>(entities.Count);

        foreach (var id in ids)
        {
            if (!entities.TryGetValue(id, out var entity))
                continue;

            var capabilities = enabledRequirements
                .Select(group =>
                {
                    var missing = group
                        .Where(requirement => !evaluator.Evaluate(requirement.Predicate, entity))
                        .Select(requirement => new EntityCompletenessMissingField(
                            requirement.RequirementId,
                            requirement.DisplayNameKey,
                            requirement.MissingMessageKey))
                        .ToList();
                    return new EntityCompletenessCapability(
                        group.Key,
                        byCode.GetValueOrDefault(group.Key, group.Key),
                        missing.Count == 0,
                        missing);
                })
                .ToList();

            results.Add(new EntityCompletenessResponseModel(entityType, id, capabilities));
        }

        return results;
    }

    /// <summary>
    /// Per-EntityType loader switch. Each branch loads the entities with
    /// AsNoTracking and no Include(): the predicates read scalar fields, and
    /// unfilled navigation collections naturally evaluate as "missing", which
    /// is the right chip answer for stub entities.
    /// </summary>
    private async Task<Dictionary<int, object>> LoadEntitiesAsync(
        string entityType, IReadOnlyList<int> ids, CancellationToken ct)
    {
        return entityType switch
        {
            "Vendor" => await db.Vendors.AsNoTracking()
                .Where(v => ids.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => (object)v, ct),
            "Part" => await db.Parts.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => (object)p, ct),
            "Customer" => await db.Customers.AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => (object)c, ct),
            _ => [],
        };
    }
}
