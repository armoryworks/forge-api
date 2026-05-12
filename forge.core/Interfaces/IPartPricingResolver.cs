using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Resolves the effective sales-price of a Part via a 4-rung fallback hierarchy:
/// <list type="number">
///     <item><description><c>PriceListEntry</c> — customer-scoped, quantity-tier match (when caller supplies customer + quantity).</description></item>
///     <item><description><c>PartPrice</c> — part-level effective-dated default. Latest EffectiveFrom wins when multiple are active.</description></item>
///     <item><description><c>VendorPartPriceTier</c> — preferred vendor's lowest-MinQuantity tier of currently-effective tiers.</description></item>
///     <item><description>Default — no source resolved; <see cref="ResolvedPartPrice.UnitPrice"/> is <c>0m</c>.</description></item>
/// </list>
/// The bulk variant (<see cref="ResolveManyAsync"/>) skips rung 1 because list-page callers
/// don't carry a customer or quantity context.
/// </summary>
public interface IPartPricingResolver
{
    /// <summary>
    /// Resolve the effective price for a single part.
    /// </summary>
    /// <param name="partId">Part to price.</param>
    /// <param name="customerId">Optional customer for rung-1 (PriceListEntry) lookup.</param>
    /// <param name="quantity">Optional requested quantity for rung-1 tier match. Required to hit a PriceListEntry whose <c>MinQuantity</c> &gt; 1.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ResolvedPartPrice> ResolveAsync(int partId, int? customerId, decimal? quantity, CancellationToken ct);

    /// <summary>
    /// Bulk variant for list-page callers. Skips rung 1 (no customer / quantity context),
    /// issues at most two queries (one for PartPrice, one for VendorPart joined to tiers),
    /// and returns one <see cref="ResolvedPartPrice"/> per requested partId. Unresolved
    /// parts get <see cref="PartPriceSource.Default"/> with <c>UnitPrice = 0m</c>.
    /// </summary>
    Task<IReadOnlyDictionary<int, ResolvedPartPrice>> ResolveManyAsync(
        IReadOnlyCollection<int> partIds, CancellationToken ct);
}
