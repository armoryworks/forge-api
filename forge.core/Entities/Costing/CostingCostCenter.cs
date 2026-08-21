using System.ComponentModel.DataAnnotations;

using Forge.Core.Enums;

namespace Forge.Core.Entities.Costing;

/// <summary>A costing cost center — a grouping for overhead pools and work centers with a shared
/// allocation basis. Distinct from the GL reporting-dimension <c>Accounting.CostCenter</c>: this one
/// carries the physical allocation drivers (square footage, headcount) and the inventoriable flag,
/// and is independent of the FULLGL <c>Book</c> so costing runs with an external GL too.</summary>
public class CostingCostCenter : BaseAuditableEntity
{
    /// <summary>Short unique code (e.g. MOLD, WHSE, OFFICE).</summary>
    [MaxLength(32)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable name.</summary>
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional parent for a support center allocated into a production center.</summary>
    public int? ParentId { get; set; }

    /// <summary>Production / Support / SGA / Warehouse — gates whether its cost is inventoriable.</summary>
    public CostCenterType Type { get; set; }

    /// <summary>Square footage — the basis for sqft-allocated shared costs (rent, utilities, tax).</summary>
    public decimal? Sqft { get; set; }

    /// <summary>Headcount — the basis for headcount-allocated shared costs (software, phone).</summary>
    public decimal? Headcount { get; set; }

    /// <summary>Whether this center's cost enters inventory (product cost) vs period cost (SG&amp;A).</summary>
    public bool IsInventoriable { get; set; } = true;

    /// <summary>Parent center, when this is a support center allocated upward.</summary>
    public CostingCostCenter? Parent { get; set; }
}
