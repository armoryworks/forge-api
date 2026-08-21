namespace Forge.Core.Models.Costing;

/// <summary>Frozen per-period cost rates for a work center.</summary>
public record WorkCenterCostRateResponseModel(
    int Id,
    int WorkCenterId,
    int CostingPeriodId,
    decimal LaborRate,
    decimal LaborOhRate,
    decimal MachineRate,
    decimal MachineOhVarRate,
    decimal MachineOhFixedRate,
    DateTime? FrozenAt,
    string? FrozenBy);
