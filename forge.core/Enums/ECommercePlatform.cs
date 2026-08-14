namespace Forge.Core.Enums;

/// <summary>
/// External systems an <see cref="Entities.ECommerceIntegration"/> can talk to.
///
/// <para>Split into two groups that behave differently downstream. On a
/// storefront you own (Shopify, WooCommerce, BigCommerce, Magento, Square) you
/// are the merchant of record: sales tax is your liability and payment runs
/// through your own processor. On a marketplace (eBay, Amazon, Etsy, Walmart)
/// the platform is a facilitator that collects and remits tax and pays you net
/// of fees on its own settlement cycle. The <see cref="SalesChannelType"/> on
/// the channel, not this enum, is what the order pipeline branches on — but the
/// two are chosen together.</para>
/// </summary>
public enum ECommercePlatform
{
    // ── Storefronts: you are the merchant of record ──
    Shopify,
    WooCommerce,
    BigCommerce,
    Magento,

    /// <summary>Square — point-of-sale and its online store. In-person retail arrives through here.</summary>
    Square,

    // ── Marketplaces: the platform is the facilitator ──
    Amazon,
    Ebay,
    Etsy,
    Walmart,

    /// <summary>
    /// No integration. Orders are keyed in by hand or loaded from a file — trade
    /// shows, phone orders, a marketplace with no connector yet. Present so a
    /// channel can exist without pretending to have credentials.
    /// </summary>
    Manual,
}
