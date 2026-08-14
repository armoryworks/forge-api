using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// One component of a <see cref="ChannelSettlement"/> — an order's proceeds, a
/// referral fee, a refund, a postage charge. Signed: income positive, fees and
/// refunds negative, so the batch reconciles by summing
/// <see cref="Amount"/> rather than by branching on type.
/// </summary>
public class ChannelSettlementLine : BaseEntity
{
    public int SettlementId { get; set; }
    public ChannelSettlement Settlement { get; set; } = null!;

    public ChannelSettlementLineType LineType { get; set; }

    /// <summary>
    /// The order this component belongs to, when the channel attributes it to
    /// one. Null for account-level charges (subscription fees, reserve moves)
    /// and for order-linked components whose order was never imported — the
    /// latter is a reconciliation exception, not a reason to drop the line.
    /// </summary>
    public int? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }

    /// <summary>The channel's order id as reported on the settlement, kept even when <see cref="SalesOrderId"/> could not be resolved.</summary>
    public string? ExternalOrderId { get; set; }

    /// <summary>Signed amount in the settlement's currency. Positive = money in, negative = money out.</summary>
    public decimal Amount { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset? PostedAt { get; set; }
}
