namespace Forge.Api.Capabilities.Discovery.Bundles;

/// <summary>
/// Pro Services rollout (Artifact 5 §3.7) — per-preset workflow-definition
/// bundle. Apply-preset upserts the contained JSON definitions into
/// <c>workflow_definitions</c>, keyed by entity type.
///
/// PRESET-08 will carry an Engagement workflow definition (intake →
/// scope → budget → SOW → kickoff). Existing presets carry null bundles.
///
/// Each JSON value must parse, reference known validator IDs, and not
/// collide with existing definitions for the same entity type. The
/// apply pipeline performs these checks before commit.
/// </summary>
/// <param name="DefinitionsByEntityType">
/// Map of entity type → workflow-definition JSON string. Entity-type
/// strings match the existing <c>WorkflowDefinition.EntityType</c>
/// column values (e.g. <c>"Job"</c>, <c>"Project"</c>, <c>"Deliverable"</c>).
/// </param>
public sealed record WorkflowDefinitionBundle(
    IReadOnlyDictionary<string, string> DefinitionsByEntityType);
