using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// Rules the portal endpoints share so the lists, the dashboard and the
/// accept/decline action agree. A quote with no ExpirationDate never
/// expires; otherwise it stays valid through the whole of its expiration
/// day in the shop's time zone (the same date rule as
/// <see cref="ClockStateRules.IsOverdue"/>). Orders and quotes carry no
/// currency of their own, so they are labelled with the active book's
/// functional currency — the currency CreateInvoice gives a new invoice.
/// </summary>
public static class PortalReadRules
{
    public static async Task<DateTimeOffset> QuoteValidFromAsync(AppDbContext db, IClock clock, CancellationToken ct)
    {
        var shopZone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        return new DateTimeOffset(ClockStateRules.LocalToday(shopZone, clock.UtcNow), TimeSpan.Zero);
    }

    public static bool IsExpired(DateTimeOffset? expirationDate, DateTimeOffset validFrom)
        => expirationDate.HasValue && expirationDate.Value < validFrom;

    public static async Task<string?> FunctionalCurrencyCodeAsync(AppDbContext db, CancellationToken ct)
    {
        var functionalId = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => b.FunctionalCurrencyId)
            .FirstOrDefaultAsync(ct) is int id and > 0 ? id : 1;

        return await db.Currencies.AsNoTracking()
            .Where(c => c.Id == functionalId)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(ct);
    }
}
