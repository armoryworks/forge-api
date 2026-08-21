using System.ComponentModel.DataAnnotations;

using Forge.Core.Enums;

namespace Forge.Core.Entities.Costing;

/// <summary>Stage-1 allocation rule: how a shared cost (matched by account pattern) splits across
/// costing cost centers — by square footage, headcount, metered reading, a direct assignment, or an
/// explicit fixed split.</summary>
public class CostAllocationRule : BaseAuditableEntity
{
    /// <summary>Pattern matching the source GL/expense account(s) this rule governs.</summary>
    [MaxLength(256)]
    public string SourceAccountPattern { get; set; } = string.Empty;

    /// <summary>The basis used to split the matched cost across cost centers.</summary>
    public AllocationBasis Basis { get; set; }

    /// <summary>JSON map of cost-center-id → share, used when <see cref="Basis"/> is FixedSplit.</summary>
    public string? SplitJson { get; set; }
}
