namespace Forge.Core.Models;

/// <summary>
/// Row model for the sales-orders list, one row per SalesOrder entity.
/// <see cref="Id"/> and <see cref="SalesOrderId"/> are both the order id;
/// <see cref="JobId"/> is null on list rows, since work orders are not listed here.
/// </summary>
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
    DateTimeOffset CreatedAt,
    int? SalesOrderId,
    int? JobId);
