namespace Forge.Api.Features.Accounting;

/// <summary>
/// Result of capitalizing a job as a fixed asset: the conversion costs absorbed into WIP and the WIP balance
/// moved to <see cref="AssetGlAccountId"/> (<see cref="Capitalized"/>; zero when the job carried no WIP).
/// </summary>
public sealed record JobCapitalizationResult(
    int JobId,
    decimal LaborAbsorbed,
    decimal OverheadAbsorbed,
    decimal SubcontractAbsorbed,
    decimal Capitalized,
    int? AssetGlAccountId,
    bool Posted);
