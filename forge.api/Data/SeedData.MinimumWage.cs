using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    /// <summary>
    /// Jurisdiction minimum wages — reference data for the piece-rate weekly
    /// make-up check (run-once on empty). States without their own minimum (or
    /// below the federal floor) are omitted: the null-state federal row is the
    /// fallback. Rates are the state base rates as of early 2025 — locality
    /// rates (city/county) and later increases are NOT tracked; admins keep
    /// their jurisdictions current. Assistance, not legal advice.
    /// </summary>
    private static async Task SeedMinimumWageRatesAsync(AppDbContext db)
    {
        if (await db.MinimumWageRates.AnyAsync()) return;

        var from = new DateOnly(2025, 1, 1);
        const string verify = "Verify against your state DOL — locality rates and mid-year increases are not tracked.";

        var rates = new List<MinimumWageRate>
        {
            new() { StateCode = null, RatePerHour = 7.25m, EffectiveFrom = new DateOnly(2009, 7, 24), Description = $"Federal (FLSA) floor. {verify}" },
        };

        (string Code, decimal Rate)[] states =
        [
            ("AK", 11.91m), ("AZ", 14.70m), ("AR", 11.00m), ("CA", 16.50m), ("CO", 14.81m),
            ("CT", 16.35m), ("DE", 15.00m), ("DC", 17.50m), ("FL", 13.00m), ("HI", 14.00m),
            ("IL", 15.00m), ("ME", 14.65m), ("MD", 15.00m), ("MA", 15.00m), ("MI", 12.48m),
            ("MN", 11.13m), ("MO", 13.75m), ("MT", 10.55m), ("NE", 13.50m), ("NV", 12.00m),
            ("NJ", 15.49m), ("NM", 12.00m), ("NY", 15.50m), ("OH", 10.70m), ("OR", 14.70m),
            ("RI", 15.00m), ("SD", 11.50m), ("VT", 14.01m), ("VA", 12.41m), ("WA", 16.66m),
            ("WV", 8.75m),
        ];
        rates.AddRange(states.Select(s => new MinimumWageRate
        {
            StateCode = s.Code,
            RatePerHour = s.Rate,
            EffectiveFrom = from,
            Description = $"{s.Code} state base rate (early 2025). {verify}",
        }));

        db.MinimumWageRates.AddRange(rates);
        await db.SaveChangesAsync();
        Log.Information("Seeded {Count} minimum-wage reference rates (federal + states)", rates.Count);
    }
}
