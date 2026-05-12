namespace Forge.Core.Entities;

/// <summary>
/// Workflow Pattern Phase 2 / D3 / D5 — Snapshot of a cost calculation at a
/// point in time. Empty until D5 lands; the schema is locked in now so the
/// future cost-recalc engine populates an existing contract instead of
/// reshaping the data model.
///
/// Polymorphic by (EntityType, EntityId). <see cref="IsCurrent"/> is true on
/// exactly one row per entity (last calc wins). The cost-bearing entity
/// references this row via <c>CurrentCostCalculationId</c>.
/// </summary>
public class CostCalculation : BaseAuditableEntity
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }

    public int ProfileId { get; set; }
    public CostingProfile Profile { get; set; } = null!;

    /// <summary>Snapshot of the profile's revision at calc time.</summary>
    public int ProfileVersion { get; set; }

    /// <summary>The headline number consumed by D3 read logic.</summary>
    public decimal ResultAmount { get; set; }

    /// <summary>JSON: direct material / direct labor / overhead breakdown.</summary>
    public string? ResultBreakdown { get; set; }

    public DateTimeOffset CalculatedAt { get; set; }

    /// <summary>Actor user (manual entry) or null (job-driven).</summary>
    public int? CalculatedBy { get; set; }

    /// <summary>True only on the latest snapshot per entity.</summary>
    public bool IsCurrent { get; set; }

    public CostCalculationInputs? Inputs { get; set; }
}
