namespace Forge.Core.Models.Accounting;

/// <summary>
/// Dispose of (sell or scrap) a fixed asset. <paramref name="ProceedsGlAccountId"/> is required when
/// <paramref name="Proceeds"/> is above zero; <paramref name="GainLossGlAccountId"/> falls back to the book's
/// GAIN_LOSS_ON_DISPOSAL account.
/// </summary>
public sealed record DisposeFixedAssetModel(
    DateOnly DisposalDate,
    decimal Proceeds = 0m,
    int? ProceedsGlAccountId = null,
    int? GainLossGlAccountId = null,
    string? Memo = null);
