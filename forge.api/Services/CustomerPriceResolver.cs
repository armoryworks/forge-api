using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// AUDIT-19-S1: resolves a part's unit price from the customer's active, in-effect price list, so
/// price lists are a LIVE input to quote/order line pricing (previously a dead input). Returns null
/// when the customer has no applicable price-list entry for the part — callers then keep whatever
/// price they already have.
/// </summary>
public class CustomerPriceResolver(AppDbContext db, IClock clock)
{
    public async Task<decimal?> ResolveUnitPriceAsync(int customerId, int partId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return await db.PriceListEntries
            .Where(e => e.PartId == partId
                && e.PriceList.CustomerId == customerId
                && e.PriceList.IsActive
                && (e.PriceList.EffectiveFrom == null || e.PriceList.EffectiveFrom <= now)
                && (e.PriceList.EffectiveTo == null || e.PriceList.EffectiveTo >= now))
            // Most-recently-effective list wins when more than one is in effect.
            .OrderByDescending(e => e.PriceList.EffectiveFrom)
            .Select(e => (decimal?)e.UnitPrice)
            .FirstOrDefaultAsync(ct);
    }
}
