namespace Forge.Core.Entities;

/// <summary>
/// Workflow Pattern Phase 2 / Q3 — Junction for cross-entity workflows.
/// One row per (run_id, entity_type, entity_id), with a free-form role
/// label ('primary', 'tax-form', 'training', etc.). The primary entity is
/// also stamped on <see cref="WorkflowRun.EntityType"/> /
/// <see cref="WorkflowRun.EntityId"/> for the common single-entity case;
/// the junction lets multi-entity flows declare additional bound entities.
///
/// Composite primary key is (RunId, EntityType, EntityId).
/// </summary>
public class WorkflowRunEntity
{
    public int RunId { get; set; }
    public WorkflowRun Run { get; set; } = null!;

    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }

    /// <summary>Role of this entity in the workflow ('primary', 'tax-form', etc.).</summary>
    public string Role { get; set; } = "primary";
}
