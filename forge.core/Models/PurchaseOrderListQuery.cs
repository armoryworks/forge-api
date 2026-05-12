using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/purchase-orders</c>. Phase 3 F7-broad /
/// WU-22 — extends the standard <see cref="PagedQuery"/> with PO-specific
/// filters.
///
/// Backward compat: existing query params (<c>vendorId</c>, <c>jobId</c>,
/// <c>status</c>) are bound here directly so legacy callers continue to work.
/// </summary>
public record PurchaseOrderListQuery : PagedQuery
{
    /// <summary>Restrict to a specific vendor.</summary>
    public int? VendorId { get; init; }

    /// <summary>Restrict to POs linked to a specific job.</summary>
    public int? JobId { get; init; }

    /// <summary>Lifecycle status (Draft / Submitted / Acknowledged / Received / Closed / Cancelled).</summary>
    public PurchaseOrderStatus? Status { get; init; }
}
