namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/sales-orders</c>. Phase 3 F1 partial / WU-18.
///
/// Architectural note: SalesOrder is treated as a query-side projection over the
/// canonical <c>Job</c> entity (Option A from phase-3-todos F1). The list endpoint
/// filters Jobs to those at "Order Confirmed" stage and downstream, then projects
/// to the existing <see cref="SalesOrderListItemModel"/> shape. Mutations remain on
/// the legacy <c>/api/v1/orders</c> surface unchanged.
/// </summary>
public record SalesOrderListQuery : PagedQuery
{
    /// <summary>Filter to SOs for a specific customer.</summary>
    public int? CustomerId { get; init; }

    /// <summary>
    /// Filter by mapped SO status (Confirmed | InProduction | Shipped | Completed | Cancelled).
    /// Maps back to the underlying Job stage code(s) — see SalesOrderProjection for details.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Date field to filter on. <c>"orderDate"</c> (default; uses Job.CreatedAt) or
    /// <c>"shipDate"</c> (uses Job.DueDate as the requested ship/delivery date).
    /// </summary>
    public string? DateField { get; init; }
}
