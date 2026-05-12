namespace Forge.Core.Models;

public record WorkCenterResponseModel(
    int Id,
    string Name,
    string Code,
    string? Description,
    decimal DailyCapacityHours,
    decimal EfficiencyPercent,
    int NumberOfMachines,
    decimal LaborCostPerHour,
    decimal BurdenRatePerHour,
    bool IsActive,
    int? AssetId,
    string? AssetName,
    int? CompanyLocationId,
    string? LocationName,
    int SortOrder);
