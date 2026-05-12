namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Create / update payload for workflow definitions.
/// </summary>
public record UpsertWorkflowDefinitionRequestModel(
    string DefinitionId,
    string EntityType,
    string DefaultMode,
    string StepsJson,
    string? ExpressTemplateComponent);
