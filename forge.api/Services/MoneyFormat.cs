using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Money, percent and quantity formatting for documents, emails and activity text. The API runs with the
/// invariant culture, where <c>{amount:C}</c> prints the generic ¤ sign; this formats with the currency's
/// symbol and decimal places from the currencies table and, like the UI's currency display, adds the ISO
/// code after the amount when the currency is not the install's base currency (<c>currency.base</c>, USD
/// when unset).
/// </summary>
public static class MoneyFormat
{
    private const string BaseCurrencyKey = "currency.base";
    private const string DefaultBaseCurrency = "USD";

    /// <summary>A formatter for <paramref name="code"/>; without a symbol the code is printed after the amount.</summary>
    public static MoneyFormatter For(string code, string? symbol, int decimalPlaces, string baseCode)
    {
        var normalized = Normalize(code);
        if (string.IsNullOrWhiteSpace(symbol) && normalized == DefaultBaseCurrency)
            symbol = "$";
        return new MoneyFormatter(
            normalized,
            string.IsNullOrWhiteSpace(symbol) ? null : symbol,
            decimalPlaces,
            normalized != Normalize(baseCode));
    }

    /// <summary>A formatter for <paramref name="currency"/>, or for the base currency when it is not known.</summary>
    public static MoneyFormatter For(Currency? currency, string baseCode)
        => currency is null
            ? For(baseCode, null, 2, baseCode)
            : For(currency.Code, currency.Symbol, currency.DecimalPlaces, baseCode);

    /// <summary>The install's base currency code, read from the same setting as <see cref="ICurrencyService.GetBaseCurrencyAsync"/>.</summary>
    public static async Task<string> BaseCodeAsync(AppDbContext db, CancellationToken ct)
    {
        var value = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == BaseCurrencyKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return Normalize(value);
    }

    /// <summary>The install's base currency code, read through <paramref name="settings"/>.</summary>
    public static async Task<string> BaseCodeAsync(ISystemSettingRepository settings, CancellationToken ct)
        => Normalize((await settings.FindByKeyAsync(BaseCurrencyKey, ct))?.Value);

    /// <summary>Loads a currency row without making it a required join of the owning query.</summary>
    public static Task<Currency?> CurrencyAsync(AppDbContext db, int currencyId, CancellationToken ct)
        => db.Set<Currency>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == currencyId, ct);

    /// <summary>The formatter of the currency with id <paramref name="currencyId"/>.</summary>
    public static async Task<MoneyFormatter> ForCurrencyIdAsync(
        AppDbContext db, int currencyId, string baseCode, CancellationToken ct)
        => For(await CurrencyAsync(db, currencyId, ct), baseCode);

    /// <summary>The formatter of the currency with id <paramref name="currencyId"/>, reading the base code itself.</summary>
    public static async Task<MoneyFormatter> ForCurrencyIdAsync(AppDbContext db, int currencyId, CancellationToken ct)
        => await ForCurrencyIdAsync(db, currencyId, await BaseCodeAsync(db, ct), ct);

    /// <summary>The formatter of the install's base currency.</summary>
    public static async Task<MoneyFormatter> ForBaseCurrencyAsync(AppDbContext db, string baseCode, CancellationToken ct)
    {
        var code = Normalize(baseCode);
        var currency = await db.Currencies.AsNoTracking()
            .Where(c => c.Code.ToUpper() == code)
            .Select(c => new { c.Symbol, c.DecimalPlaces })
            .FirstOrDefaultAsync(ct);
        return For(code, currency?.Symbol, currency?.DecimalPlaces ?? 2, code);
    }

    /// <summary>The formatter of the install's base currency, reading the base code itself.</summary>
    public static async Task<MoneyFormatter> ForBaseCurrencyAsync(AppDbContext db, CancellationToken ct)
        => await ForBaseCurrencyAsync(db, await BaseCodeAsync(db, ct), ct);

    /// <summary>The formatter of the install's base currency, with the code read through <paramref name="settings"/>.</summary>
    public static async Task<MoneyFormatter> ForBaseCurrencyAsync(
        AppDbContext db, ISystemSettingRepository settings, CancellationToken ct)
        => await ForBaseCurrencyAsync(db, await BaseCodeAsync(settings, ct), ct);

    /// <summary>The formatter of the install's base currency, with the code from <paramref name="currency"/>.</summary>
    public static async Task<MoneyFormatter> ForBaseCurrencyAsync(
        AppDbContext db, ICurrencyService currency, CancellationToken ct)
        => await ForBaseCurrencyAsync(db, await currency.GetBaseCurrencyAsync(ct), ct);

    /// <summary>
    /// The formatter for amounts that span <paramref name="currencyIds"/>: that currency when there is one, the base
    /// currency when there is none, and the base currency with its code printed when they differ.
    /// </summary>
    public static async Task<MoneyFormatter> ForCurrencyIdsAsync(
        AppDbContext db, IEnumerable<int> currencyIds, CancellationToken ct)
    {
        var distinct = currencyIds.Distinct().ToList();
        var baseCode = await BaseCodeAsync(db, ct);
        return distinct.Count == 1
            ? await ForCurrencyIdAsync(db, distinct[0], baseCode, ct)
            : distinct.Count == 0
                ? await ForBaseCurrencyAsync(db, baseCode, ct)
                : (await ForBaseCurrencyAsync(db, baseCode, ct)).WithCode();
    }

    /// <summary>A customer payment's currency: that of the invoices it is applied to (applications are taken 1:1 off invoice balances).</summary>
    public static async Task<MoneyFormatter> ForCustomerPaymentAsync(AppDbContext db, int paymentId, CancellationToken ct)
        => await ForCurrencyIdsAsync(
            db,
            await db.PaymentApplications.AsNoTracking()
                .Where(a => a.PaymentId == paymentId)
                .Select(a => a.Invoice.CurrencyId)
                .ToListAsync(ct),
            ct);

    /// <summary>A vendor payment's currency: that of the bills it pays (applications are taken 1:1 off bill balances).</summary>
    public static async Task<MoneyFormatter> ForVendorPaymentAsync(AppDbContext db, int vendorPaymentId, CancellationToken ct)
        => await ForCurrencyIdsAsync(
            db,
            await db.VendorPaymentApplications.AsNoTracking()
                .Where(a => a.VendorPaymentId == vendorPaymentId)
                .Select(a => a.VendorBill.CurrencyId)
                .ToListAsync(ct),
            ct);

    /// <summary>A rate such as 0.0725 as "7.25%", without rounding to whole percent.</summary>
    public static string Percent(decimal rate, int maxDecimalPlaces = 4)
        => (rate * 100m).ToString("#,##0." + new string('#', Math.Clamp(maxDecimalPlaces, 0, 8)), CultureInfo.InvariantCulture) + "%";

    /// <summary>A quantity with thousands separators and no trailing zeros, e.g. 1940.0000 as "1,940".</summary>
    public static string Quantity(decimal quantity)
        => quantity.ToString("#,##0.####", CultureInfo.InvariantCulture);

    private static string Normalize(string? code)
        => string.IsNullOrWhiteSpace(code) ? DefaultBaseCurrency : code.Trim().ToUpperInvariant();
}
