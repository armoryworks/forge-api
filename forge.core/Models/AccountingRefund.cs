namespace Forge.Core.Models;

/// <summary>Money paid back to a customer, pushed to the connected accounting provider as a refund receipt.</summary>
public record AccountingRefund(
    string CustomerExternalId,
    string RefNumber,
    decimal Amount,
    DateTimeOffset Date,
    string Method,
    string Description,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null,
    string? ItemExternalId = null,
    string? DepositAccountExternalId = null);
