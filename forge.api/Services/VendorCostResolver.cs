using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Default <see cref="IVendorCostResolver"/>. Reads the preferred vendor's currently-effective
/// price tiers (with each tier's purchase unit content), and for the requested base quantity
/// picks the option whose **cost per base unit** (tier price ÷ content) is cheapest — quantity-break
/// aware. A tier with no purchase unit is treated as priced per base unit (content = 1), which
/// preserves the legacy single-option behavior. All reads are <c>AsNoTracking</c>.
/// </summary>
public class VendorCostResolver(AppDbContext db) : IVendorCostResolver
{
    private const string DefaultCurrency = "USD";

    public async Task<ResolvedBaseUnitCost> ResolveAsync(int partId, decimal requestedBaseQty, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var qty = requestedBaseQty <= 0 ? 1m : requestedBaseQty;

        var tiers = await db.VendorPartPriceTiers
            .AsNoTracking()
            .Where(t => t.VendorPart.PartId == partId
                && t.VendorPart.IsPreferred
                && t.EffectiveFrom <= now
                && (t.EffectiveTo == null || t.EffectiveTo > now))
            .Select(t => new TierRow(
                t.Id,
                t.UnitPrice,
                t.Currency,
                t.MinQuantity,
                t.PurchaseUnitId,
                t.PurchaseUnit != null ? (decimal?)t.PurchaseUnit.ContentQuantity : null))
            .ToListAsync(ct);

        if (tiers.Count == 0)
            return new ResolvedBaseUnitCost(partId, 0m, DefaultCurrency, null, 1m, 0m, null, Resolved: false);

        ResolvedBaseUnitCost? best = null;

        // Each option (and the null/per-base-unit "option") is costed independently; the cheapest
        // per-base-unit wins. Quantity-break: for an option holding `content` base units, covering
        // `qty` needs ceil(qty/content) of them — pick the best break that volume qualifies for.
        foreach (var group in tiers.GroupBy(t => t.PurchaseUnitId))
        {
            var content = group.First().Content ?? 1m;
            if (content <= 0) continue; // malformed option — skip rather than divide by zero

            var optionsNeeded = Math.Ceiling(qty / content);

            var applicable = group.Where(t => t.MinQuantity <= optionsNeeded)
                                  .OrderByDescending(t => t.MinQuantity)
                                  .FirstOrDefault()
                              ?? group.OrderBy(t => t.MinQuantity).First();

            var costPerBase = applicable.UnitPrice / content;

            if (best is null || costPerBase < best.CostPerBaseUnit)
            {
                best = new ResolvedBaseUnitCost(
                    partId,
                    costPerBase,
                    applicable.Currency,
                    applicable.PurchaseUnitId,
                    content,
                    applicable.UnitPrice,
                    applicable.Id,
                    Resolved: true);
            }
        }

        // Every group was malformed (content ≤ 0) — nothing usable resolved.
        return best ?? new ResolvedBaseUnitCost(partId, 0m, DefaultCurrency, null, 1m, 0m, null, Resolved: false);
    }

    private sealed record TierRow(
        int Id, decimal UnitPrice, string Currency, decimal MinQuantity, int? PurchaseUnitId, decimal? Content);
}
