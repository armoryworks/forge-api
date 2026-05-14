namespace Forge.Platform.Enums;

/// <summary>
/// Local-side classification of the depreciation method to apply to an asset.
/// Selected at creation; the actual depreciation schedule and posting is delegated
/// to the external accounting provider. Phase 3 F4.
/// </summary>
public enum DepreciationMethod
{
    StraightLine,
    DecliningBalance,
    UnitsOfProduction
}
