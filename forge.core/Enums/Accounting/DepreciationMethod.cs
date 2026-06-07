namespace Forge.Core.Enums.Accounting;

/// <summary>Fixed-asset depreciation method. Straight-line for v1; declining-balance/units later.</summary>
public enum DepreciationMethod
{
    StraightLine,
}

/// <summary>Lifecycle of a fixed asset.</summary>
public enum FixedAssetStatus
{
    Active,
    FullyDepreciated,
    Disposed,
}
