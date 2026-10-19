using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// AUDIT-19-S1: resolves a part's unit price from the customer's active, in-effect price list, so
/// price lists are a LIVE input to quote/order line pricing (previously a dead input). Returns null
/// when the customer has no applicable price-list entry for the part — callers then keep whatever
/// price they already have.
/// <para>
/// The most-recently-effective list wins; a list with no start date ranks below dated lists. Within
/// that list the quantity break applies: the entry with the highest <c>MinQuantity</c> at or below
/// the line quantity, or the lowest break when the quantity is not known yet. Entries priced in a
/// currency other than the install's base currency are skipped, because quote and order lines carry
/// no currency and would otherwise read a foreign amount as a base-currency one.
/// </para>
/// </summary>
public class CustomerPriceResolver(AppDbContext db, IClock clock, ICurrencyService currency)
{
    public async Task<decimal?> ResolveUnitPriceAsync(int customerId, int partId, decimal? quantity, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var baseCurrency = (await currency.GetBaseCurrencyAsync(ct)).Trim().ToUpperInvariant();

        var entries = db.PriceListEntries
            .Where(e => e.PartId == partId
                && e.PriceList.CustomerId == customerId
                && e.PriceList.IsActive
                && (e.PriceList.EffectiveFrom == null || e.PriceList.EffectiveFrom <= now)
                && (e.PriceList.EffectiveTo == null || e.PriceList.EffectiveTo >= now)
                && e.Currency.ToUpper() == baseCurrency);

        if (quantity is decimal qty)
            entries = entries.Where(e => e.MinQuantity <= qty);

        var byList = entries
            .OrderBy(e => e.PriceList.EffectiveFrom == null)
            .ThenByDescending(e => e.PriceList.EffectiveFrom)
            .ThenByDescending(e => e.PriceListId);

        var byBreak = quantity is null
            ? byList.ThenBy(e => e.MinQuantity)
            : byList.ThenByDescending(e => e.MinQuantity);

        return await byBreak
            .Select(e => (decimal?)e.UnitPrice)
            .FirstOrDefaultAsync(ct);
    }
}
