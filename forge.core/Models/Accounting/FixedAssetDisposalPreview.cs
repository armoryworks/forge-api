namespace Forge.Core.Models.Accounting;

/// <summary>
/// What disposing of a fixed asset on a date would post, without posting: the catch-up depreciation for
/// unposted months (or, for units-of-production, the shots not yet depreciated), the net book value written off,
/// and the gain (positive) or loss (negative) at the given proceeds.
/// </summary>
public sealed record FixedAssetDisposalPreview(
    int FixedAssetId,
    DateOnly DisposalDate,
    decimal CatchUpDepreciation,
    decimal NetBookValue,
    decimal Proceeds,
    decimal GainLoss);
