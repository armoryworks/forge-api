namespace Forge.Core.Enums;

/// <summary>
/// Classifies how a <see cref="Entities.SalesChannel"/> reaches its buyer. This
/// is the discriminator the order pipeline branches on — it decides whether the
/// credit gate runs, whether a quote is expected upstream, who is liable for
/// sales tax, and whether the order carries an end-consumer identity separate
/// from the account that owes the money.
/// </summary>
public enum SalesChannelType
{
    /// <summary>
    /// Classic account business. The <see cref="Entities.Customer"/> on the
    /// order IS the buyer: credit terms, customer PO, price lists and the
    /// quote→order progression all apply. Every pre-channel sales order is
    /// this type.
    /// </summary>
    DirectB2B,

    /// <summary>
    /// Consumer sales where you are the merchant of record — your own web
    /// store, point of sale, trade-show and phone orders. Payment is captured
    /// at order time and sales tax is your own liability, but the buyer is a
    /// consumer rather than an account, so the order carries a
    /// <see cref="Entities.RetailBuyer"/> and settles against a house account.
    /// </summary>
    DirectRetail,

    /// <summary>
    /// Third-party marketplace (eBay, Amazon, Etsy, Walmart). Like
    /// <see cref="DirectRetail"/>, plus two divergences that change the money:
    /// the marketplace is the facilitator that collects and remits sales tax,
    /// and it pays out net of fees on its own settlement cycle rather than per
    /// order.
    /// </summary>
    Marketplace,
}
