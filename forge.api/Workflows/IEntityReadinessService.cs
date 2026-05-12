using Forge.Core.Entities;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — Per-entity-type loaders that pull the entity
/// with whatever relations the readiness validators need to inspect. Each
/// entity type registers an implementation of <see cref="IEntityReadinessLoader"/>
/// (Phase 3 wires Part; later phases wire customer/quote/etc.).
///
/// The service itself orchestrates: load entity → fetch validators →
/// evaluate predicates → return missing list.
/// </summary>
public interface IEntityReadinessService
{
    /// <summary>
    /// Evaluates all stored validators for an entity instance. Returns the
    /// list of validators whose predicates currently fail; an empty list
    /// means the entity is ready to be promoted out of Draft.
    /// </summary>
    Task<IReadOnlyList<EntityReadinessValidator>> GetMissingValidatorsAsync(
        string entityType, int entityId, CancellationToken ct);
}

/// <summary>
/// Per-entity-type adapter: knows how to load the entity row + relations
/// needed by the readiness predicates. Implementations are scoped services
/// keyed by entity-type string.
/// </summary>
public interface IEntityReadinessLoader
{
    /// <summary>The entity-type string this loader covers (e.g. "Part").</summary>
    string EntityType { get; }

    /// <summary>Loads the entity (with relations) or null if not found / soft-deleted.</summary>
    Task<object?> LoadAsync(int entityId, CancellationToken ct);
}
