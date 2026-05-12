using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    private static async Task SeedSalesTaxRatesAsync(AppDbContext db)
    {
        if (await db.SalesTaxRates.AnyAsync()) return;

        var epoch = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var seedsDir = Path.Combine(AppContext.BaseDirectory, "Data", "Seeds");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // US state-level base rates. Rates are STATE base rate only — local (county/city)
        // rates add on top. Zero-rate states (OR, MT, NH, DE, AK) are included for
        // completeness so the system returns 0% automatically rather than falling back
        // to the default. Source: Tax Foundation + Sales Tax Handbook.
        var states = Deserialize<List<StateSalesTaxSeed>>(
            Path.Combine(seedsDir, "sales-tax-rates.json"), opts);

        var rates = states.Select(s => new SalesTaxRate
        {
            Name = $"{s.Name} Sales Tax",
            Code = s.Code,
            StateCode = s.Code,
            Rate = s.Rate,
            EffectiveFrom = epoch,
            EffectiveTo = null,
            IsDefault = false,
            IsActive = true,
            Description = s.Rate == 0m
                ? $"{s.Name}: no state sales tax. Verify if local rates apply."
                : $"{s.Name} state base rate. Add local rates for your nexus jurisdictions.",
        }).ToList();

        rates.Add(new SalesTaxRate
        {
            Name = "Default (No Tax)",
            Code = "DEFAULT",
            StateCode = null,
            Rate = 0.0000m,
            EffectiveFrom = epoch,
            EffectiveTo = null,
            IsDefault = true,
            IsActive = true,
            Description = "Fallback rate when no state-specific rate is found. Update to your default jurisdiction rate.",
        });

        db.SalesTaxRates.AddRange(rates);
        await db.SaveChangesAsync();
        Log.Information("Seeded {Count} sales tax rates ({States} states + default)",
            rates.Count, states.Count);
    }

    private sealed class StateSalesTaxSeed
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal Rate { get; set; }
    }
}
