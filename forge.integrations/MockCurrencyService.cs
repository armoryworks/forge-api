using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockCurrencyService(ILogger<MockCurrencyService> logger) : ICurrencyService
{
    public Task<decimal> GetExchangeRateAsync(int fromCurrencyId, int toCurrencyId, DateOnly date, CancellationToken ct)
    {
        logger.LogInformation("[MockCurrency] GetExchangeRate from {From} to {To} on {Date}",
            fromCurrencyId, toCurrencyId, date);
        return Task.FromResult(1.0m);
    }

    public Task<decimal> ConvertAsync(decimal amount, int fromCurrencyId, int toCurrencyId, DateOnly date, CancellationToken ct)
    {
        logger.LogInformation("[MockCurrency] Convert {Amount} from {From} to {To} on {Date}",
            amount, fromCurrencyId, toCurrencyId, date);
        return Task.FromResult(amount);
    }

    public Task<int> GetBaseCurrencyIdAsync(CancellationToken ct)
    {
        logger.LogInformation("[MockCurrency] GetBaseCurrencyId");
        return Task.FromResult(1);
    }

    public Task<decimal> CalculateExchangeGainLossAsync(decimal invoiceAmount, decimal invoiceRate, decimal paymentRate, CancellationToken ct)
    {
        logger.LogInformation("[MockCurrency] CalculateExchangeGainLoss invoice={Amount} invoiceRate={InvoiceRate} paymentRate={PaymentRate}",
            invoiceAmount, invoiceRate, paymentRate);
        return Task.FromResult(0m);
    }

    public Task FetchExchangeRatesAsync(DateOnly date, CancellationToken ct)
    {
        logger.LogInformation("[MockCurrency] FetchExchangeRates for {Date}", date);
        return Task.CompletedTask;
    }

    public Task<string> GetBaseCurrencyAsync(CancellationToken ct)
    {
        logger.LogInformation("[MockCurrency] GetBaseCurrency");
        return Task.FromResult("USD");
    }
}
