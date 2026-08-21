namespace Forge.Core.Models.Costing;

/// <summary>An overhead pool with its behavior and driver.</summary>
public record OverheadPoolResponseModel(
    int Id,
    int CostingCostCenterId,
    int? WorkCenterId,
    string Code,
    string Name,
    string Behavior,
    decimal? FixedPortion,
    string Driver);
