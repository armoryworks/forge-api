using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Forge.Core.Enums;

namespace Forge.Core.Entities.Costing;

/// <summary>
/// One overhead category in a <see cref="CostingTemplate"/>: how the pool is
/// created (code/name/behavior/driver), how its amount is asked and annualized
/// (<see cref="AmountBasis"/> + optional default), and which GL expense account
/// mirrors it when full GL is on.
/// </summary>
public class CostingTemplateLine : BaseAuditableEntity
{
    public int CostingTemplateId { get; set; }

    [MaxLength(32)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public OverheadBehavior Behavior { get; set; }

    public OverheadDriver Driver { get; set; }

    public CostingAmountBasis AmountBasis { get; set; }

    /// <summary>Pre-filled answer offered at apply time (e.g. 7.65 for employer FICA).</summary>
    public decimal? DefaultValue { get; set; }

    /// <summary>GL expense account to mirror this category into (created if missing).</summary>
    [MaxLength(16)]
    public string? GlAccountNumber { get; set; }

    [MaxLength(128)]
    public string? GlAccountName { get; set; }

    public int SortOrder { get; set; }

    [ForeignKey(nameof(CostingTemplateId))]
    public CostingTemplate? CostingTemplate { get; set; }
}
