using Forge.Core.Enums;

namespace Forge.Core.Models.Costing;

/// <summary>One overhead category of a costing template.</summary>
public record CostingTemplateLineModel(
    int Id,
    string Code,
    string Name,
    OverheadBehavior Behavior,
    OverheadDriver Driver,
    CostingAmountBasis AmountBasis,
    decimal? DefaultValue,
    string? GlAccountNumber,
    string? GlAccountName,
    int SortOrder);
