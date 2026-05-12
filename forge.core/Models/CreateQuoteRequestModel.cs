namespace Forge.Core.Models;

public record CreateQuoteRequestModel(
    int CustomerId,
    int? ShippingAddressId,
    DateTimeOffset? ExpirationDate,
    string? Notes,
    decimal TaxRate,
    List<CreateQuoteLineModel> Lines);

public record CreateQuoteLineModel(
    int? PartId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Notes);
