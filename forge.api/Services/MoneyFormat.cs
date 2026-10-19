using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Currency formatting for customer-facing documents. The API runs with the invariant culture, where
/// <c>{amount:C}</c> prints the generic ¤ sign; this formats with the install's base currency
/// (<c>currency.base</c>, USD when unset) and that currency's symbol and decimal places from the
/// currencies table.
/// </summary>
public static class MoneyFormat
{
    private const string BaseCurrencyKey = "currency.base";

    /// <summary>A currency format for <paramref name="code"/>; without a symbol the code itself is printed.</summary>
    public static NumberFormatInfo For(string code, string? symbol, int decimalPlaces = 2)
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.CurrencySymbol = string.IsNullOrWhiteSpace(symbol) ? $"{code} " : symbol;
        format.CurrencyDecimalDigits = decimalPlaces;
        return format;
    }

    /// <summary>The currency format of the install's base currency.</summary>
    public static async Task<NumberFormatInfo> ForBaseCurrencyAsync(
        AppDbContext db, ISystemSettingRepository settings, CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(BaseCurrencyKey, ct);
        var code = string.IsNullOrWhiteSpace(setting?.Value) ? "USD" : setting.Value.Trim().ToUpperInvariant();

        var currency = await db.Currencies.AsNoTracking()
            .Where(c => c.Code.ToUpper() == code)
            .Select(c => new { c.Symbol, c.DecimalPlaces })
            .FirstOrDefaultAsync(ct);

        var symbol = currency?.Symbol;
        if (string.IsNullOrWhiteSpace(symbol) && code == "USD")
            symbol = "$";
        return For(code, symbol, currency?.DecimalPlaces ?? 2);
    }
}
