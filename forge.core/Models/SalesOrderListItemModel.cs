namespace Forge.Core.Models;

/// <summary>
/// Unified row model for the sales-orders list, which merges two sources:
/// Draft rows project from the SalesOrder entity, while confirmed/production
/// rows project from the Job read model. <see cref="Id"/> is therefore only a
/// row identity (paging/trackBy/deep-link key) — it is a SalesOrder id for
/// Draft rows but a Job id for Job-projected rows. Use <see cref="SalesOrderId"/>
/// to open the sales-order detail and <see cref="JobId"/> to open the job detail.
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
