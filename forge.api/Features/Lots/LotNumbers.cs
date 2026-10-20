using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Lots;

/// <summary>
/// The house lot number, LOT-yyyyMMdd-NNN on the shop's local date, used whenever a lot record is made without a
/// lot number. The sequence continues after the highest number already used that day, deleted lots included,
/// and skips the numbers in <c>reserved</c> (ones handed out earlier in the same request but not saved yet).
/// </summary>
public static class LotNumbers
{
    public static async Task<string> NextAsync(
        AppDbContext db, IClock clock, CancellationToken ct, ICollection<string>? reserved = null)
    {
        var shopToday = ClockStateRules.LocalToday(await ClockStateRules.ShopTimeZoneAsync(db, ct), clock.UtcNow);
        var prefix = $"LOT-{shopToday:yyyyMMdd}-";
        var used = await db.LotRecords
            .IgnoreQueryFilters()
            .Where(l => l.LotNumber.StartsWith(prefix))
            .Select(l => l.LotNumber)
            .ToListAsync(ct);
        if (reserved is not null)
            used.AddRange(reserved.Where(n => n.StartsWith(prefix, StringComparison.Ordinal)));

        var highest = used
            .Select(n => int.TryParse(n.AsSpan(prefix.Length), out var seq) ? seq : 0)
            .DefaultIfEmpty(0)
            .Max();
        var next = $"{prefix}{highest + 1:D3}";
        reserved?.Add(next);
        return next;
    }
}
