using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Phase-4 — fixed-asset register + monthly depreciation posting (Dr depreciation expense / Cr accumulated
/// depreciation). Straight-line by default, declining-balance on request; units-of-production by shot count for
/// company-owned molds (§10.3). Idempotent per asset per month. CAP-ACCT-DEPRECIATION gated at the edge.
/// </summary>
public interface IDepreciationService
{
    Task<FixedAssetModel> CreateAssetAsync(CreateFixedAssetModel model, CancellationToken ct = default);
    Task<IReadOnlyList<FixedAssetModel>> ListAssetsAsync(int bookId, CancellationToken ct = default);

    /// <summary>One fixed asset with its GL accounts and its posted and projected depreciation schedule.</summary>
    Task<FixedAssetDetailModel> GetAssetAsync(int fixedAssetId, CancellationToken ct = default);

    /// <summary>Edits a fixed asset; refused once any depreciation has posted for it or it is no longer active.</summary>
    Task<FixedAssetDetailModel> UpdateAssetAsync(int fixedAssetId, UpdateFixedAssetModel model, CancellationToken ct = default);

    /// <summary>What <see cref="RunDepreciationAsync"/> would post for the month, without posting.</summary>
    Task<DepreciationPreviewResult> PreviewDepreciationAsync(int bookId, DateOnly periodMonth, CancellationToken ct = default);

    /// <summary>Company-owned operational assets a fixed asset may book, with the fixed asset already linked to each.</summary>
    Task<IReadOnlyList<LinkableAssetModel>> ListLinkableAssetsAsync(int bookId, CancellationToken ct = default);

    /// <summary>Posts one month of depreciation for every eligible active asset in the book. Idempotent.</summary>
    Task<DepreciationRunResult> RunDepreciationAsync(
        int bookId, DateOnly periodMonth, int postedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a fixed asset: posts one catch-up depreciation journal for the months before the disposal
    /// month that were never run, then one disposal journal (Dr accumulated depreciation, Dr proceeds, Cr asset
    /// cost, gain or loss to balance) and marks the asset Disposed so later runs skip it.
    /// </summary>
    Task<FixedAssetDisposalResult> DisposeAssetAsync(
        int fixedAssetId, DisposeFixedAssetModel model, int disposedByUserId, CancellationToken ct = default);
}
