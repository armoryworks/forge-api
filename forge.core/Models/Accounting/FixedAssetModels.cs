using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>
/// Register a depreciable fixed asset. Straight-line by default; units-of-production (§10.3 — molds
/// depreciate by shot count) additionally requires <paramref name="UsefulLifeUnits"/> and
/// <paramref name="LinkedAssetId"/> (the operational asset supplying the shot counter).
/// </summary>
public sealed record CreateFixedAssetModel(
    int BookId,
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

/// <summary>A fixed asset + its derived depreciation figures.</summary>
public sealed record FixedAssetModel(
    int Id,
    int BookId,
    string Name,
    string? AssetTag,
    decimal Cost,
    decimal SalvageValue,
    DateOnly InServiceDate,
    int UsefulLifeMonths,
    decimal MonthlyDepreciation,
    decimal AccumulatedDepreciation,
    decimal NetBookValue,
    FixedAssetStatus Status);

/// <summary>Result of a monthly depreciation run.</summary>
public sealed record DepreciationRunResult(int BookId, DateOnly PeriodMonth, int AssetsDepreciated, decimal TotalAmount);
