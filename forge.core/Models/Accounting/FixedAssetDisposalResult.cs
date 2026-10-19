namespace Forge.Core.Models.Accounting;

/// <summary>
/// Result of a fixed-asset disposal: the catch-up depreciation posted for unposted months before the disposal
/// month, the net book value written off, and the gain (positive) or loss (negative) recognized.
/// </summary>
public sealed record FixedAssetDisposalResult(
    FixedAssetModel Asset,
    long? JournalEntryId,
    decimal CatchUpDepreciation,
    decimal NetBookValue,
    decimal Proceeds,
    decimal GainLoss);
