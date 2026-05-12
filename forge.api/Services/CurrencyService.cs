using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

/// <summary>
/// Default <see cref="ICurrencyService"/> implementation. The FX-conversion
/// methods are still stubs (<see cref="GetExchangeRateAsync"/> returns 1.0,
/// <see cref="ConvertAsync"/> returns the input amount, etc. — same shape
/// as the mock) until a real FX layer ships. The new
/// <see cref="GetBaseCurrencyAsync"/> reads <c>currency.base</c> from
/// <c>system_settings</c> with a 5-minute <see cref="IMemoryCache"/> TTL,
/// since base currency changes ~never.
/// </summary>
public class CurrencyService(
    ISystemSettingRepository settingRepo,
    IMemoryCache cache,
    ILogger<CurrencyService> logger) : ICurrencyService
{
    private const string CacheKey = "currency.base";
    private const string DefaultBaseCurrency = "USD";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

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

    // ── FX stubs ──────────────────────────────────────────────────────────────
    // These mirror MockCurrencyService until a real FX layer ships. The
    // PartPricingResolver does not call any of these — currency is per-record,
    // and the UI surfaces the inline ISO code on mismatch instead of converting.

    public Task<decimal> GetExchangeRateAsync(int fromCurrencyId, int toCurrencyId, DateOnly date, CancellationToken ct)
    {
        logger.LogInformation("[Currency] GetExchangeRate from {From} to {To} on {Date} (stub returns 1.0)",
            fromCurrencyId, toCurrencyId, date);
        return Task.FromResult(1.0m);
    }

    public Task<decimal> ConvertAsync(decimal amount, int fromCurrencyId, int toCurrencyId, DateOnly date, CancellationToken ct)
    {
        logger.LogInformation("[Currency] Convert {Amount} from {From} to {To} on {Date} (stub returns input)",
            amount, fromCurrencyId, toCurrencyId, date);
        return Task.FromResult(amount);
    }

    public Task<int> GetBaseCurrencyIdAsync(CancellationToken ct)
    {
        logger.LogInformation("[Currency] GetBaseCurrencyId (stub returns 1)");
        return Task.FromResult(1);
    }

    public Task<decimal> CalculateExchangeGainLossAsync(decimal invoiceAmount, decimal invoiceRate, decimal paymentRate, CancellationToken ct)
    {
        logger.LogInformation("[Currency] CalculateExchangeGainLoss invoice={Amount} invoiceRate={InvoiceRate} paymentRate={PaymentRate} (stub returns 0)",
            invoiceAmount, invoiceRate, paymentRate);
        return Task.FromResult(0m);
    }

    public Task FetchExchangeRatesAsync(DateOnly date, CancellationToken ct)
    {
        logger.LogInformation("[Currency] FetchExchangeRates for {Date} (stub no-op)", date);
        return Task.CompletedTask;
    }
}
