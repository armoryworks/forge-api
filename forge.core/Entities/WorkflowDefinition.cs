namespace Forge.Core.Entities;

/// <summary>
/// Workflow Pattern Phase 2 / D6 — Workflow definitions stored as data so
/// new variants and step orderings ship without a code release. Steps and
/// per-step completion gates live in <see cref="StepsJson"/>; the runtime
/// pulls validator metadata from <see cref="EntityReadinessValidator"/>
/// rows when evaluating gate references.
///
/// <see cref="Id"/> includes a version suffix (e.g. "part-assembly-guided-v1")
/// per Q2: in-flight runs stay pinned on the version they started with.
/// </summary>
public class WorkflowDefinition : BaseAuditableEntity
{
    /// <summary>
    /// Stable id including version, e.g. "part-assembly-guided-v1". This is
    /// the natural key — soft-delete via <see cref="BaseAuditableEntity.DeletedAt"/>.
    /// The numeric Id is the surrogate primary key for FK/indexing only.
    /// </summary>
    public string DefinitionId { get; set; } = string.Empty;

    /// <summary>Entity type the workflow targets, e.g. "Part".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>'express' | 'guided' — initial mode for new runs.</summary>
    public string DefaultMode { get; set; } = "guided";

    /// <summary>
    /// Ordered step list as JSON. Each element is a
    /// <see cref="WorkflowStepDefinition"/>:
    /// <c>{ id, labelKey, componentName, required, completionGates: [validatorId] }</c>.
    /// </summary>
    public string StepsJson { get; set; } = "[]";

    /// <summary>UI component name for the express form, e.g. "PartExpressFormComponent".</summary>
    public string? ExpressTemplateComponent { get; set; }

    /// <summary>True when this row was inserted by the seeder.</summary>
    public bool IsSeedData { get; set; }
}
