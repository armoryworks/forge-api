
namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/invoices</c>. Phase 3 F7-broad / WU-22 —
/// extends the standard <see cref="PagedQuery"/> with invoice-specific filters.
/// </summary>
public record InvoiceListQuery : PagedQuery
{
    /// <summary>Restrict to a specific customer.</summary>
    public int? CustomerId { get; init; }

    /// <summary>Lifecycle status (Draft / Sent / PartiallyPaid / Paid / Void).</summary>
    public InvoiceStatus? Status { get; init; }
}
