namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 3 — Workflow definition response model.
/// </summary>
public record WorkflowDefinitionResponseModel(
    int Id,
    string DefinitionId,
    string EntityType,
    string DefaultMode,
    string StepsJson,
    string? ExpressTemplateComponent,
    bool IsSeedData);
