namespace Forge.Core.Models;

public record SalesOrderListItemModel(
    int Id,
    string OrderNumber,
    int CustomerId,
    string CustomerName,
    string Status,
    string? CustomerPO,
    int LineCount,
    decimal Total,
    DateTimeOffset? RequestedDeliveryDate,
    DateTimeOffset CreatedAt);
