namespace Forge.Core.Models;

/// <summary>
/// Workflow Pattern Phase 2 / D6 — The shape stored inside
/// <c>WorkflowDefinition.StepsJson</c>. Each step references entity
/// readiness validators by id (no inline predicates — single source of
/// truth lives in <c>EntityReadinessValidator</c>).
/// </summary>
public record WorkflowStepDefinition(
    string Id,
    string LabelKey,
    string ComponentName,
    bool Required,
    IReadOnlyList<string> CompletionGates);
