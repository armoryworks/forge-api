using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using Forge.Api.Services.Fx;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Default <see cref="ICurrencyService"/> implementation. Exchange rates come from the manual
/// <c>exchange_rates</c> table through <see cref="DocumentCurrencyResolver"/> (latest rate on or before the
/// date, inverse, then triangulated through the functional currency); a missing rate throws
/// <see cref="FxRateMissingException"/>. <see cref="FetchExchangeRatesAsync"/> stays a no-op: there is no
/// automatic rate feed. <see cref="GetBaseCurrencyAsync"/> reads <c>currency.base</c> from
/// <c>system_settings</c> with a 5-minute <see cref="IMemoryCache"/> TTL, since base currency changes ~never.
/// </summary>
public class CurrencyService(
    ISystemSettingRepository settingRepo,
    IMemoryCache cache,
    ILogger<CurrencyService> logger,
    AppDbContext db) : ICurrencyService
{
    private const string CacheKey = "currency.base";
    private const string DefaultBaseCurrency = "USD";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly DocumentCurrencyResolver _rates = new(db);

    public async Task<string> GetBaseCurrencyAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        var setting = await settingRepo.FindByKeyAsync(CacheKey, ct);
        var value = !string.IsNullOrWhiteSpace(setting?.Value) ? setting.Value : DefaultBaseCurrency;

        cache.Set(CacheKey, value, CacheTtl);
        return value;
    }

    public async Task<decimal> GetExchangeRateAsync(int fromCurrencyId, int toCurrencyId, DateOnly date, CancellationToken ct)
        => (await _rates.GetRateAsync(fromCurrencyId, toCurrencyId, date, ct)).Rate;

    public async Task<decimal> ConvertAsync(decimal amount, int fromCurrencyId, int toCurrencyId, DateOnly date, CancellationToken ct)
    {
        if (fromCurrencyId == toCurrencyId)
            return amount;

        var rate = await GetExchangeRateAsync(fromCurrencyId, toCurrencyId, date, ct);
        var decimals = await db.Currencies.AsNoTracking()
            .Where(c => c.Id == toCurrencyId)
            .Select(c => (int?)c.DecimalPlaces)
            .FirstOrDefaultAsync(ct) ?? 2;
        return Math.Round(amount * rate, decimals, MidpointRounding.AwayFromZero);
    }

    public Task<int> GetBaseCurrencyIdAsync(CancellationToken ct) => _rates.GetFunctionalCurrencyIdAsync(ct);

    public Task<decimal> CalculateExchangeGainLossAsync(decimal invoiceAmount, decimal invoiceRate, decimal paymentRate, CancellationToken ct)
        => Task.FromResult(
            Math.Round(invoiceAmount * paymentRate, 2, MidpointRounding.AwayFromZero)
            - Math.Round(invoiceAmount * invoiceRate, 2, MidpointRounding.AwayFromZero));

    public Task FetchExchangeRatesAsync(DateOnly date, CancellationToken ct)
    {
        logger.LogInformation("[Currency] FetchExchangeRates for {Date}: no automatic rate feed is configured; rates are entered under Admin > Currencies", date);
        return Task.CompletedTask;
    }
}
