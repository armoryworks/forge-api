using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Forge.Core.Enums;

namespace Forge.Core.Entities.Costing;

/// <summary>Flat item-level burden — the routing-less shop's escape hatch for putting overhead onto a
/// part without a work center. Applied in the cost roll as a percent of material or a per-unit amount.</summary>
public class ItemBurden : BaseAuditableEntity
{
    /// <summary>The part (item) this burden applies to.</summary>
    public int ItemId { get; set; }

    /// <summary>The costing period this burden applies to.</summary>
    public int CostingPeriodId { get; set; }

    /// <summary>Which cost element this burden lands in (MOH or MOHF).</summary>
    public CostElement Element { get; set; }

    /// <summary>Basis: "pct_of_mat" (fraction of material cost) or "per_unit" (flat amount).</summary>
    [MaxLength(16)]
    public string Basis { get; set; } = "per_unit";

    /// <summary>The burden value — a fraction when pct_of_mat, an amount when per_unit.</summary>
    public decimal Value { get; set; }

    /// <summary>The costing period.</summary>
    [ForeignKey(nameof(CostingPeriodId))]
    public CostingPeriod? CostingPeriod { get; set; }
}
