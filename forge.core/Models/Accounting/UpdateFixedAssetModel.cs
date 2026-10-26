using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>
/// Edit a registered fixed asset. Allowed only while the asset is active and no depreciation has posted for
/// it; the same rules as registering apply (units-of-production needs <paramref name="UsefulLifeUnits"/> and
/// <paramref name="LinkedAssetId"/>, customer-owned assets are refused).
/// </summary>
public sealed record UpdateFixedAssetModel(
    string Name,
    string? AssetTag,
    decimal Cost,
    decimal SalvageValue,
    DateOnly InServiceDate,
    int UsefulLifeMonths,
    int AssetGlAccountId,
    int AccumulatedDepreciationGlAccountId,
    int DepreciationExpenseGlAccountId,
    DepreciationMethod Method = DepreciationMethod.StraightLine,
    decimal? UsefulLifeUnits = null,
    int? LinkedAssetId = null);
