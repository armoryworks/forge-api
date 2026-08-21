namespace Forge.Core.Models.Costing;

/// <summary>A costing cost center with its allocation drivers.</summary>
public record CostingCostCenterResponseModel(
    int Id,
    string Code,
    string Name,
    int? ParentId,
    string Type,
    decimal? Sqft,
    decimal? Headcount,
    bool IsInventoriable);
