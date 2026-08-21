using System.ComponentModel.DataAnnotations.Schema;

namespace Forge.Core.Entities.Costing;

/// <summary>A part's frozen standard cost for a costing period — the 8-element decomposition at this
/// level and rolled up, plus the total. This is the Tier-3 period-frozen record (distinct from the
/// live Tier-1/2 <c>CostCalculation</c>).</summary>
public class ItemStandardCost : BaseAuditableEntity
{
    /// <summary>The part (item) this standard is for.</summary>
    public int ItemId { get; set; }

    /// <summary>The costing period this standard is frozen for.</summary>
    public int CostingPeriodId { get; set; }

    /// <summary>JSON of this-level cost elements {MAT, MOH, LAB, LOH, MCH, MOHV, MOHF, SUB}.</summary>
    public string ThisLevelJson { get; set; } = string.Empty;

    /// <summary>JSON of rolled-up cost elements (this level + lower levels).</summary>
    public string RolledUpJson { get; set; } = string.Empty;

    /// <summary>Total standard cost (sum of the rolled-up elements).</summary>
    public decimal TotalStandard { get; set; }

    /// <summary>Standard lot size used to amortize setup into the per-unit standard.</summary>
    public decimal StandardLotSize { get; set; }

    /// <summary>Cost-to-sell = cost-to-make plus the SG&amp;A/financing load (see spec §6).</summary>
    public decimal? CostToSell { get; set; }

    /// <summary>When this standard was rolled.</summary>
    public DateTime? RolledAt { get; set; }

    /// <summary>Roll version — increments on each re-freeze, retaining prior rolls.</summary>
    public int RollVersion { get; set; }

    /// <summary>The costing period.</summary>
    [ForeignKey(nameof(CostingPeriodId))]
    public CostingPeriod? CostingPeriod { get; set; }
}
