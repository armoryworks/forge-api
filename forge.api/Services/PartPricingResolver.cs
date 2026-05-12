using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Default <see cref="IPartPricingResolver"/> implementation. Walks a 4-rung
/// fallback hierarchy: customer-scoped PriceListEntry → effective PartPrice
/// → preferred VendorPartPriceTier → default 0. All queries use
/// <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{T}"/>.
/// </summary>
public class PartPricingResolver(AppDbContext db) : IPartPricingResolver
{
    private const string DefaultCurrency = "USD";

    public async Task<ResolvedPartPrice> ResolveAsync(
        int partId, int? customerId, decimal? quantity, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Rung 1 — customer-scoped PriceListEntry. Need both a customer and a
        // quantity (the latter so we can pick the right MinQuantity tier).
        if (customerId.HasValue)
        {
            var qty = quantity ?? 1m;

            // Find the customer's currently-effective PriceList. Treat
            // null EffectiveFrom as "always effective from the start".
            var priceListId = await db.PriceLists
                .AsNoTracking()
                .Where(pl => pl.CustomerId == customerId.Value
                    && pl.IsActive
                    && (pl.EffectiveFrom == null || pl.EffectiveFrom <= now)
                    && (pl.EffectiveTo == null || pl.EffectiveTo > now))
                .Select(pl => (int?)pl.Id)
                .FirstOrDefaultAsync(ct);

            if (priceListId.HasValue)
            {
                var entry = await db.PriceListEntries
                    .AsNoTracking()
                    .Where(e => e.PriceListId == priceListId.Value
                        && e.PartId == partId
                        && e.MinQuantity <= qty)
                    .OrderByDescending(e => e.MinQuantity)
                    .Select(e => new { e.Id, e.UnitPrice, e.Currency, e.Notes })
                    .FirstOrDefaultAsync(ct);

                if (entry is not null)
                {
                    return new ResolvedPartPrice(
                        PartId: partId,
                        UnitPrice: entry.UnitPrice,
                        Currency: entry.Currency,
                        Source: PartPriceSource.PriceListEntry,
                        SourceRowId: entry.Id,
                        Notes: entry.Notes);
                }
            }
        }

        // Rung 2 — part-level effective-dated default sales price.
        var partPrice = await db.PartPrices
            .AsNoTracking()
            .Where(pp => pp.PartId == partId
                && pp.EffectiveFrom <= now
                && (pp.EffectiveTo == null || pp.EffectiveTo > now))
            .OrderByDescending(pp => pp.EffectiveFrom)
            .Select(pp => new { pp.Id, pp.UnitPrice, pp.Currency, pp.Notes })
            .FirstOrDefaultAsync(ct);

        if (partPrice is not null)
        {
            return new ResolvedPartPrice(
                PartId: partId,
                UnitPrice: partPrice.UnitPrice,
                Currency: partPrice.Currency,
                Source: PartPriceSource.PartPrice,
                SourceRowId: partPrice.Id,
                Notes: partPrice.Notes);
        }

        // Rung 3 — preferred vendor's lowest-MinQuantity currently-effective tier.
        var vendorTier = await db.VendorPartPriceTiers
            .AsNoTracking()
            .Where(t => t.VendorPart.PartId == partId
                && t.VendorPart.IsPreferred
                && t.EffectiveFrom <= now
                && (t.EffectiveTo == null || t.EffectiveTo > now))
            .OrderBy(t => t.MinQuantity)
            .Select(t => new { t.Id, t.UnitPrice, t.Currency, t.Notes })
            .FirstOrDefaultAsync(ct);

        if (vendorTier is not null)
        {
            return new ResolvedPartPrice(
                PartId: partId,
                UnitPrice: vendorTier.UnitPrice,
                Currency: vendorTier.Currency,
                Source: PartPriceSource.VendorPartTier,
                SourceRowId: vendorTier.Id,
                Notes: vendorTier.Notes);
        }

        // Rung 4 — default.
        return new ResolvedPartPrice(
            PartId: partId,
            UnitPrice: 0m,
            Currency: DefaultCurrency,
            Source: PartPriceSource.Default,
            SourceRowId: null,
            Notes: null);
    }

    public async Task<IReadOnlyDictionary<int, ResolvedPartPrice>> ResolveManyAsync(
        IReadOnlyCollection<int> partIds, CancellationToken ct)
    {
        if (partIds.Count == 0)
            return new Dictionary<int, ResolvedPartPrice>();

        // De-dup defensively.
        var idList = partIds.Distinct().ToList();
        var now = DateTimeOffset.UtcNow;

        // Rung 2 — bulk PartPrice. Pull all currently-effective rows for the
        // requested ids; group + pick latest EffectiveFrom per part in memory.
        var partPriceRows = await db.PartPrices
            .AsNoTracking()
            .Where(pp => idList.Contains(pp.PartId)
                && pp.EffectiveFrom <= now
                && (pp.EffectiveTo == null || pp.EffectiveTo > now))
            .Select(pp => new
            {
                pp.Id,
                pp.PartId,
                pp.UnitPrice,
                pp.Currency,
                pp.EffectiveFrom,
                pp.Notes,
            })
            .ToListAsync(ct);

        var partPriceByPart = partPriceRows
            .GroupBy(r => r.PartId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.EffectiveFrom).First());

        // Rung 3 — bulk preferred VendorPart tiers. One query joining VendorPart
        // to its currently-effective tiers, restricted to preferred rows for the
        // requested partIds. Group + pick lowest MinQuantity per partId.
        var preferredTierRows = await db.VendorPartPriceTiers
            .AsNoTracking()
            .Where(t => idList.Contains(t.VendorPart.PartId)
                && t.VendorPart.IsPreferred
                && t.EffectiveFrom <= now
                && (t.EffectiveTo == null || t.EffectiveTo > now))
            .Select(t => new
            {
                t.Id,
                PartId = t.VendorPart.PartId,
                t.UnitPrice,
                t.Currency,
                t.MinQuantity,
                t.Notes,
            })
            .ToListAsync(ct);

        var vendorTierByPart = preferredTierRows
            .GroupBy(r => r.PartId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.MinQuantity).First());

        var result = new Dictionary<int, ResolvedPartPrice>(idList.Count);

        foreach (var id in idList)
        {
            if (partPriceByPart.TryGetValue(id, out var pp))
            {
                result[id] = new ResolvedPartPrice(
                    PartId: id,
                    UnitPrice: pp.UnitPrice,
                    Currency: pp.Currency,
                    Source: PartPriceSource.PartPrice,
                    SourceRowId: pp.Id,
                    Notes: pp.Notes);
                continue;
            }

            if (vendorTierByPart.TryGetValue(id, out var vt))
            {
                result[id] = new ResolvedPartPrice(
                    PartId: id,
                    UnitPrice: vt.UnitPrice,
                    Currency: vt.Currency,
                    Source: PartPriceSource.VendorPartTier,
                    SourceRowId: vt.Id,
                    Notes: vt.Notes);
                continue;
            }

            result[id] = new ResolvedPartPrice(
                PartId: id,
                UnitPrice: 0m,
                Currency: DefaultCurrency,
                Source: PartPriceSource.Default,
                SourceRowId: null,
                Notes: null);
        }

        return result;
    }
}
