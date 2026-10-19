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
/// currency of their own, so they are labelled with the customer's
/// DefaultCurrency, else the active book's functional currency — the
/// currency CreateInvoice gives a new invoice.
/// </summary>
public static class PortalReadRules
{
    public const int ListCap = 200;

    public const string TotalCountHeader = "X-Total-Count";

    public static async Task<int> TotalCountAsync<T>(IQueryable<T> scope, int fetched, CancellationToken ct)
        => fetched < ListCap ? fetched : await scope.CountAsync(ct);

    public static async Task<DateTimeOffset> QuoteValidFromAsync(AppDbContext db, IClock clock, CancellationToken ct)
    {
        var shopZone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        return new DateTimeOffset(ClockStateRules.LocalToday(shopZone, clock.UtcNow), TimeSpan.Zero);
    }

    public static bool IsExpired(DateTimeOffset? expirationDate, DateTimeOffset validFrom)
        => expirationDate.HasValue && expirationDate.Value < validFrom;

    public static async Task<string?> CustomerCurrencyCodeAsync(AppDbContext db, int customerId, CancellationToken ct)
    {
        var customerDefault = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => c.DefaultCurrency)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(customerDefault)
            ? await FunctionalCurrencyCodeAsync(db, ct)
            : customerDefault.Trim().ToUpperInvariant();
    }

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

public record PortalList<T>(List<T> Items, int TotalCount);
