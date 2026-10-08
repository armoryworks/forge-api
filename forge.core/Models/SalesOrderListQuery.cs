namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/sales-orders</c>, a paged list of
/// SalesOrder entities in the <see cref="SalesOrderListItemModel"/> shape.
/// </summary>
public record SalesOrderListQuery : PagedQuery
{
    /// <summary>Filter to SOs for a specific customer.</summary>
    public int? CustomerId { get; init; }

    /// <summary>
    /// Filter by SalesOrderStatus name (Draft | Confirmed | InProduction | PartiallyShipped |
    /// Shipped | Completed | Cancelled), case-insensitive. An unknown value matches no rows.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Date field to filter on. <c>"orderDate"</c> (default; uses SalesOrder.CreatedAt) or
    /// <c>"shipDate"</c> (uses SalesOrder.RequestedDeliveryDate).
    /// </summary>
    public string? DateField { get; init; }
}
