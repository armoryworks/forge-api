namespace Forge.Core.Models.Accounting;

/// <summary>
/// Dry run of a month's depreciation for a book: what <c>depreciation/run</c> would post for the same month,
/// asset by asset, without posting anything.
/// </summary>
public sealed record DepreciationPreviewResult(
    int BookId,
    DateOnly PeriodMonth,
    IReadOnlyList<DepreciationPreviewLine> Lines,
    decimal TotalAmount);

/// <summary>One asset's charge in a depreciation preview.</summary>
public sealed record DepreciationPreviewLine(
    int FixedAssetId,
    string Name,
    string? AssetTag,
    decimal Amount,
    decimal NetBookValueAfter);

/// <summary>
/// A company-owned operational asset that a fixed asset may book. <paramref name="LinkedFixedAssetId"/> is the
/// fixed asset already booking it, if any.
/// </summary>
public sealed record LinkableAssetModel(
    int Id,
    string Name,
    string? SerialNumber,
    decimal? AcquisitionCost,
    int CurrentShotCount,
    int? LinkedFixedAssetId);
