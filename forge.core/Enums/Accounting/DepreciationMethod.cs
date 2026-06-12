namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Fixed-asset depreciation method. Straight-line is the default/fallback; units-of-production
/// depreciates by consumed units (shot count for company-owned molds — §10.3). Declining-balance later.
/// </summary>
public enum DepreciationMethod
{
    StraightLine,
    UnitsOfProduction,
}

/// <summary>Lifecycle of a fixed asset.</summary>
public enum FixedAssetStatus
{
    Active,
    FullyDepreciated,
    Disposed,
}
