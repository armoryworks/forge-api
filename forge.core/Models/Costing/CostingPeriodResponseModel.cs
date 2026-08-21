namespace Forge.Core.Models.Costing;

/// <summary>A costing period with its lifecycle state.</summary>
public record CostingPeriodResponseModel(
    int Id,
    DateTime StartDate,
    DateTime EndDate,
    string Status,
    DateTime? FrozenAt,
    DateTime? ClosedAt);
