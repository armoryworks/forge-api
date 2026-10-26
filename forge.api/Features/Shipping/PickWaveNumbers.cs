using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public static class PickWaveNumbers
{
    public static async Task<string> NextAsync(AppDbContext db, CancellationToken ct)
    {
        var lastId = await db.PickWaves.IgnoreQueryFilters().AsNoTracking()
            .OrderByDescending(w => w.Id)
            .Select(w => (int?)w.Id)
            .FirstOrDefaultAsync(ct);
        return $"WAVE-{(lastId ?? 0) + 1:D4}";
    }
}
