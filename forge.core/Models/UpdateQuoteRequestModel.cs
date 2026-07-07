namespace Forge.Core.Models;

public record UpdateQuoteRequestModel(
    int? ShippingAddressId,
    DateTimeOffset? ExpirationDate,
    string? Notes,
    decimal? TaxRate,
    string? CustomerPO = null);
