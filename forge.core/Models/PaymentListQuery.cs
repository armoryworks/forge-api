using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/payments</c>. Phase 3 F7-broad / WU-22 —
/// extends the standard <see cref="PagedQuery"/> with payment-specific filters.
/// </summary>
public record PaymentListQuery : PagedQuery
{
    /// <summary>Restrict to a specific customer.</summary>
    public int? CustomerId { get; init; }

    /// <summary>Filter by payment method (Cash / Check / CreditCard / ACH / Wire / Other).</summary>
    public PaymentMethod? PaymentMethod { get; init; }
}
