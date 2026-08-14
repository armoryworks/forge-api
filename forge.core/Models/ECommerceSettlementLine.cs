using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// One component of a payout. Signed: income positive, fees and refunds
/// negative, so a batch reconciles by summing rather than by branching on type.
/// </summary>
public record ECommerceSettlementLine
{
    public ChannelSettlementLineType LineType { get; init; }

    /// <summary>The platform's order id, when the component is attributable to an order.</summary>
    public string? ExternalOrderId { get; init; }

    public decimal Amount { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? PostedAt { get; init; }
}
