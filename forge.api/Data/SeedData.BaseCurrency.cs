using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    /// <summary>
    /// Makes the book's functional currency the single source of the base currency. When an active book
    /// exists, <c>currencies.is_base_currency</c> and the <c>currency.base</c> setting are set to match it,
    /// with a warning when they disagreed. Runs on every boot; a no-op when they already agree or no book exists.
    /// </summary>
    internal static async Task EnsureBaseCurrencyConsistentAsync(AppDbContext db)
    {
        var functionalId = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => (int?)b.FunctionalCurrencyId)
            .FirstOrDefaultAsync();
        if (functionalId is not int baseId)
            return;

        var currencies = await db.Currencies.ToListAsync();
        var functional = currencies.FirstOrDefault(c => c.Id == baseId);
        if (functional is null)
            return;

        var changed = false;
        foreach (var currency in currencies.Where(c => c.IsBaseCurrency != (c.Id == baseId)))
        {
            Log.Warning(
                "[CURRENCY] Currency {Code} base flag was {Flag}; the book's functional currency is {Functional}. Correcting it.",
                currency.Code, currency.IsBaseCurrency, functional.Code);
            currency.IsBaseCurrency = currency.Id == baseId;
            changed = true;
        }

        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "currency.base");
        if (setting is not null && !string.Equals(setting.Value, functional.Code, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                "[CURRENCY] Setting currency.base was {Setting}; the book's functional currency is {Functional}. Correcting it.",
                setting.Value, functional.Code);
            setting.Value = functional.Code;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync();
    }
}
