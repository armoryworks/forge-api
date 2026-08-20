namespace Forge.Core.Models;

public record UpdateQuoteRequestModel(
    int? ShippingAddressId,
    DateTimeOffset? ExpirationDate,
    string? Notes,
    decimal? TaxRate,
    string? CustomerPO = null,
    // Optional editable quote number — changeable only while the quote is Draft
    // and only when the quotes.allow_manual_numbers setting is on.
    string? QuoteNumber = null);
