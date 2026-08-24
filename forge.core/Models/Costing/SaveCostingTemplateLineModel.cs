using Forge.Core.Enums;

namespace Forge.Core.Models.Costing;

/// <summary>One overhead-category line in a costing-template save payload.</summary>
public sealed record SaveCostingTemplateLineModel(
    string Code,
    string Name,
    OverheadBehavior Behavior,
    OverheadDriver Driver,
    CostingAmountBasis AmountBasis,
    decimal? DefaultValue,
    string? GlAccountNumber,
    string? GlAccountName);
