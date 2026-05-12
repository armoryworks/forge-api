using System.Text.Json;

namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Start a new workflow run. The server creates
/// the entity row with <c>status='Draft'</c> AND the workflow_run row in a
/// single SaveChanges. <see cref="InitialEntityData"/> is an opaque jsonb
/// payload of fields applied at create time — Phase 3 wires the Part variant
/// (description, partType, material, …) and other entity types follow the
/// same shape.
/// </summary>
public record StartWorkflowRunRequestModel(
    string EntityType,
    string DefinitionId,
    string? Mode,
    JsonElement? InitialEntityData);
