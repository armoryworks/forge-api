using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Default <see cref="ITariffResolver"/>. Reads from <c>tariff_rates</c>
/// using the (HtsCode, CountryOfOrigin) lookup with effective-window
/// filtering. Today the table ships empty so the resolver always returns
/// 0 — landed-cost duty stays at <c>—</c> until an admin imports broker
/// data. The query is AsNoTracking + indexed, so a populated table
/// resolves a single rate in one round trip.
/// </summary>
public class TariffResolver(AppDbContext db) : ITariffResolver
{
    public async Task<decimal> ResolveAsync(
        string? htsCode,
        string? countryOfOrigin,
        DateOnly receiptDate,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(htsCode) || string.IsNullOrWhiteSpace(countryOfOrigin))
            return 0m;

        // SCD-2 effective-window pick. Latest EffectiveFrom that is on or
        // before the receipt date AND whose EffectiveTo is null or after
        // the receipt date.
        var rate = await db.TariffRates
            .AsNoTracking()
            .Where(t => t.HtsCode == htsCode
                && t.CountryOfOrigin == countryOfOrigin
                && t.EffectiveFrom <= receiptDate
                && (t.EffectiveTo == null || t.EffectiveTo > receiptDate))
            .OrderByDescending(t => t.EffectiveFrom)
            .Select(t => (decimal?)t.RatePct)
            .FirstOrDefaultAsync(ct);

        return rate ?? 0m;
    }
}
