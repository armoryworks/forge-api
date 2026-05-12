using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — Default <see cref="IEntityReadinessService"/>
/// implementation. Looks up the per-entity-type loader from the registered
/// set, fetches all stored validators for the entity type, and runs the
/// shared <see cref="PredicateEvaluator"/> against the loaded entity.
/// </summary>
public class EntityReadinessService(
    AppDbContext db,
    PredicateEvaluator evaluator,
    IEnumerable<IEntityReadinessLoader> loaders,
    ILogger<EntityReadinessService> logger) : IEntityReadinessService
{
    private readonly Dictionary<string, IEntityReadinessLoader> _loaders =
        loaders.ToDictionary(l => l.EntityType, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<EntityReadinessValidator>> GetMissingValidatorsAsync(
        string entityType, int entityId, CancellationToken ct)
    {
        if (!_loaders.TryGetValue(entityType, out var loader))
        {
            logger.LogWarning("[WORKFLOW] No readiness loader registered for entity type {Type}", entityType);
            // Without a loader we cannot evaluate; treat all validators as failing
            // so the caller surfaces a clear error rather than silently passing.
            return await db.EntityReadinessValidators
                .AsNoTracking()
                .Where(v => v.EntityType == entityType)
                .ToListAsync(ct);
        }

        var entity = await loader.LoadAsync(entityId, ct);
        if (entity is null)
        {
            // Missing entity ⇒ all validators fail (caller handles 404 / 409 mapping).
            return await db.EntityReadinessValidators
                .AsNoTracking()
                .Where(v => v.EntityType == entityType)
                .ToListAsync(ct);
        }

        var validators = await db.EntityReadinessValidators
            .AsNoTracking()
            .Where(v => v.EntityType == entityType)
            .ToListAsync(ct);

        var missing = new List<EntityReadinessValidator>();
        foreach (var v in validators)
        {
            // Per-record applicability: if the validator carries an
            // applicability predicate, evaluate it FIRST. False ⇒ this
            // validator doesn't apply to this record (e.g. hasIncoterms
            // skipped on a domestic-only customer) and is excluded from
            // both the evaluation pass and the missing-validators reply.
            // NULL applicability ⇒ always-applicable, preserves the
            // pre-applicability behavior on every shipped validator.
            if (!string.IsNullOrWhiteSpace(v.ApplicabilityPredicate)
                && !evaluator.Evaluate(v.ApplicabilityPredicate, entity))
            {
                continue;
            }

            if (!evaluator.Evaluate(v.Predicate, entity))
                missing.Add(v);
        }
        return missing;
    }
}
