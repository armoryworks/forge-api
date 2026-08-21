namespace Forge.Core.Costing;

/// <summary>The roll result: this-level and rolled-up element breakdowns and the total standard.</summary>
public sealed record CostRollResult(
    CostElementAmounts ThisLevel,
    CostElementAmounts RolledUp,
    decimal TotalStandard);
