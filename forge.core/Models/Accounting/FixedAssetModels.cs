using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>Register a depreciable fixed asset.</summary>
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
    int DepreciationExpenseGlAccountId);

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
