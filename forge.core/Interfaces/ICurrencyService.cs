namespace Forge.Core.Interfaces;

public interface ICurrencyService
{
    Task<decimal> GetExchangeRateAsync(int fromCurrencyId, int toCurrencyId, DateOnly date, CancellationToken ct);
    Task<decimal> ConvertAsync(decimal amount, int fromCurrencyId, int toCurrencyId, DateOnly date, CancellationToken ct);
    Task<int> GetBaseCurrencyIdAsync(CancellationToken ct);
    Task<decimal> CalculateExchangeGainLossAsync(decimal invoiceAmount, decimal invoiceRate, decimal paymentRate, CancellationToken ct);
    Task FetchExchangeRatesAsync(DateOnly date, CancellationToken ct);

    /// <summary>
    /// Returns the install's base currency (ISO-4217 code, e.g. <c>USD</c>).
    /// Reads the <c>currency.base</c> system_setting; falls back to <c>USD</c>
    /// when unset. Cached briefly because the value changes ~never.
    /// </summary>
    Task<string> GetBaseCurrencyAsync(CancellationToken ct);
}
