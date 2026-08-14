namespace Forge.Core.Enums;

/// <summary>
/// What a single component of a marketplace payout represents. A settlement is
/// gross order proceeds less a stack of these, and reconciliation depends on
/// classifying each one — revenue, contra-revenue and expense land in different
/// places, and only <see cref="OrderProceeds"/> ties back to a sales order.
/// </summary>
public enum ChannelSettlementLineType
{
    /// <summary>Gross amount billed to the buyer for an order. Positive. Ties to a <see cref="Entities.SalesOrder"/>.</summary>
    OrderProceeds,

    /// <summary>Shipping the buyer paid, when the channel reports it separately from the item total.</summary>
    ShippingIncome,

    /// <summary>
    /// Sales tax the marketplace collected. Pass-through — the marketplace
    /// remits it, so it is neither income nor the install's payable. Present in
    /// the settlement only so the arithmetic reconciles.
    /// </summary>
    TaxCollected,

    /// <summary>Commission / referral fee the channel charged. Negative — an operating expense.</summary>
    ReferralFee,

    /// <summary>Fulfilment, storage or pick-pack fees (FBA and equivalents). Negative.</summary>
    FulfillmentFee,

    /// <summary>Postage bought through the channel. Negative.</summary>
    ShippingLabel,

    /// <summary>Money returned to a buyer. Negative — contra-revenue, and links to an order when the channel says which.</summary>
    Refund,

    /// <summary>Subscription, listing or account-level charges not attributable to one order. Negative.</summary>
    ChannelFee,

    /// <summary>Reserve held back or released by the channel. Sign varies.</summary>
    ReserveAdjustment,

    /// <summary>Anything the connector could not classify. Forces manual review rather than a silent mis-post.</summary>
    Other,
}
