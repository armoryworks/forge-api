namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Fixed-asset depreciation method. Straight-line is the default/fallback; units-of-production
/// depreciates by consumed units (shot count for company-owned molds — §10.3); declining-balance charges
/// twice the straight-line rate on net book value, switching to straight-line once that charges more.
/// </summary>
public enum DepreciationMethod
{
    StraightLine,
    UnitsOfProduction,
    DecliningBalance,
}

/// <summary>Lifecycle of a fixed asset.</summary>
public enum FixedAssetStatus
{
    Active,
    FullyDepreciated,
    Disposed,
}
