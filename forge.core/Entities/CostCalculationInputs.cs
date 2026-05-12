namespace Forge.Core.Entities;

/// <summary>
/// Workflow Pattern Phase 2 / D3 / D5 — Inputs that produced a
/// <see cref="CostCalculation"/>. 1:1 with the parent calc row. Common
/// inputs are structured columns (queryable / indexable / migratable);
/// tier-3 ABC pools and custom drivers go in <see cref="CustomInputs"/>
/// (jsonb).
///
/// Avoids the single-blob anti-pattern: queryable bulk reporting on the
/// common cases stays cheap, while exotic ABC inputs don't force a schema
/// change.
/// </summary>
public class CostCalculationInputs : BaseAuditableEntity
{
    public int CostCalculationId { get; set; }
    public CostCalculation CostCalculation { get; set; } = null!;

    public decimal? DirectMaterialCost { get; set; }
    public decimal? DirectLaborHours { get; set; }
    public decimal? DirectLaborCost { get; set; }
    public decimal? MachineHours { get; set; }
    public decimal? OverheadAmount { get; set; }
    public decimal? OverheadRatePct { get; set; }

    /// <summary>JSON: pool/driver inputs for ABC, plus user-defined drivers.</summary>
    public string? CustomInputs { get; set; }
}
