namespace Forge.Core.Entities;

/// <summary>
/// Workflow Pattern Phase 2 / D3 — Costing-mode discriminator (flat /
/// departmental / abc) and rate parameters. Per-install resolution priority:
/// record override → entity-type default → active profile mode.
///
/// Profile rows are versioned by <see cref="EffectiveFrom"/> /
/// <see cref="EffectiveTo"/> dates; the active profile at calc time is
/// snapshotted into <see cref="CostCalculation.ProfileId"/> +
/// <see cref="CostCalculation.ProfileVersion"/>.
/// </summary>
public class CostingProfile : BaseAuditableEntity
{
    /// <summary>Stable identifier (e.g. "default", "precision-shop-2026").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>'flat' | 'departmental' | 'abc'.</summary>
    public string Mode { get; set; } = "flat";

    /// <summary>For Tier-1 flat mode: single overhead percent applied uniformly.</summary>
    public decimal? FlatRatePct { get; set; }

    /// <summary>JSON: <c>[{ cost_center_id, rate_pct }]</c> for departmental mode.</summary>
    public string? DepartmentalRates { get; set; }

    /// <summary>JSON: <c>[{ pool_id, name, total_amount, driver, allocation }]</c> for ABC mode.</summary>
    public string? Pools { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}
