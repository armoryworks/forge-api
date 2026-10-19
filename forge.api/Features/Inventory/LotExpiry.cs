using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

/// <summary>
/// Lot expiry on stock-out. A lot's expiration date is a calendar date: the lot is still good on that date and is
/// expired from the next shop-local day. Issuing an expired lot to a job is refused with <see cref="Refusal"/>.
/// </summary>
public static class LotExpiry
{
    public static async Task<DateOnly> ShopTodayAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct)
        => DateOnly.FromDateTime(ClockStateRules.LocalToday(await ClockStateRules.ShopTimeZoneAsync(db, ct), now));

    public static bool IsExpired(DateTimeOffset? expirationDate, DateOnly shopToday)
        => expirationDate is DateTimeOffset date && DateOnly.FromDateTime(date.UtcDateTime) < shopToday;

    public static async Task<Dictionary<string, DateOnly>> ExpiredLotsAsync(
        AppDbContext db, int partId, DateOnly shopToday, CancellationToken ct)
    {
        var lots = await db.LotRecords.AsNoTracking()
            .Where(l => l.PartId == partId && l.ExpirationDate != null)
            .Select(l => new { l.LotNumber, l.ExpirationDate })
            .ToListAsync(ct);
        return lots
            .Where(l => IsExpired(l.ExpirationDate, shopToday))
            .GroupBy(l => l.LotNumber)
            .ToDictionary(g => g.Key, g => g.Min(l => DateOnly.FromDateTime(l.ExpirationDate!.Value.UtcDateTime)));
    }

    public static async Task<DateOnly?> ExpiredOnAsync(
        AppDbContext db, int partId, string? lotNumber, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(lotNumber))
            return null;
        var expired = await ExpiredLotsAsync(db, partId, await ShopTodayAsync(db, now, ct), ct);
        return expired.TryGetValue(lotNumber, out var date) ? date : null;
    }

    public static LotExpiredException Refusal(string lotNumber, DateOnly expiredOn)
        => new($"Lot {lotNumber} expired on {expiredOn:yyyy-MM-dd}.");
}
