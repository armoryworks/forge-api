namespace Forge.Core.Costing;

/// <summary>One routing operation as the cost roll sees it. Times are per unit; setup is per lot and
/// amortized across the item's standard lot size.</summary>
public sealed record CostRollRoutingOp(
    int WorkCenterId,
    decimal SetupHours,
    decimal RunHoursPerUnit,
    decimal LaborCrew,
    decimal SubcontractStdCost);
