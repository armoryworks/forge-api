namespace Forge.Core.Models;

public record QuoteDetailResponseModel(
    int Id,
    string QuoteNumber,
    int CustomerId,
    string CustomerName,
    int? ShippingAddressId,
    string Status,
    DateTimeOffset? SentDate,
    DateTimeOffset? ExpirationDate,
    DateTimeOffset? AcceptedDate,
    string? Notes,
    decimal TaxRate,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    int? SalesOrderId,
    string? SalesOrderNumber,
    int? SourceEstimateId,
    List<QuoteLineResponseModel> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CustomerPO = null);
