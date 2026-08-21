namespace Forge.Core.Costing;

/// <summary>Frozen per-period rates for the work center an operation runs on.</summary>
public sealed record CostRollWorkCenterRate(
    decimal LaborRate,
    decimal LaborOhRate,
    decimal MachineRate,
    decimal MachineOhVarRate,
    decimal MachineOhFixedRate);
