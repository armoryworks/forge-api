using System.Text.Json;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — Per-entity-type adapter that creates an
/// entity row in <c>status='Draft'</c> at workflow start, using whatever
/// initial fields the client supplied. Returns the new entity id (the
/// workflow run row pins this id).
///
/// Phase 3 wires the Part variant; later phases add customer / quote /
/// vendor / etc.
/// </summary>
public interface IWorkflowEntityCreator
{
    /// <summary>The entity-type string this creator covers (e.g. "Part").</summary>
    string EntityType { get; }

    /// <summary>
    /// Creates a draft entity row using the supplied initial data and
    /// returns the new id. Does NOT call SaveChanges — the workflow handler
    /// orchestrates a single SaveChanges across both the entity and the
    /// workflow_run row.
    /// </summary>
    Task<int> CreateDraftAsync(JsonElement? initialData, CancellationToken ct);
}

/// <summary>
/// Workflow Pattern Phase 3 — Per-entity-type adapter for applying step
/// fields to an existing entity row. Mirrors <see cref="IWorkflowEntityCreator"/>;
/// each entity type registers an implementation.
/// </summary>
public interface IWorkflowFieldApplier
{
    string EntityType { get; }

    /// <summary>
    /// Applies the step-field payload to the entity in-place. Each entity
    /// type interprets the JSON payload according to its own schema.
    /// </summary>
    Task ApplyAsync(int entityId, JsonElement fields, CancellationToken ct);

    /// <summary>
    /// Soft-deletes a draft entity (used on abandon). Returns true if the
    /// entity was Draft and is now soft-deleted; false otherwise (already
    /// promoted, already deleted, not found, etc.).
    /// </summary>
    Task<bool> SoftDeleteIfDraftAsync(int entityId, CancellationToken ct);
}
