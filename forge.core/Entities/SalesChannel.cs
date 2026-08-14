using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Core.Entities;

/// <summary>
/// A route to market. Every <see cref="SalesOrder"/> belongs to exactly one
/// channel, and the channel's <see cref="ChannelType"/> is what the order
/// pipeline branches on for the retail-vs-account divergences (credit gate,
/// quote expectation, tax liability, settlement shape).
///
/// <para><b>Why this exists rather than a flag on Customer.</b> The
/// pre-channel model had one counterparty per order — the
/// <see cref="Customer"/> both owed the money and received the goods. Retail
/// splits those roles: on a marketplace the platform owes you the money while
/// a consumer receives the goods. The channel names the arrangement and holds
/// the <see cref="SoldToCustomer"/> house account that carries the AR, so
/// <see cref="SalesOrder.CustomerId"/> stays non-nullable and every existing
/// AR / statement / accounting-sync query keeps working untouched.</para>
///
/// <para><b>Default channel.</b> Exactly one channel may carry
/// <see cref="IsDefault"/> (filtered unique index). A null
/// <see cref="SalesOrder.ChannelId"/> resolves to it — the same "null = the
/// default row" convention <see cref="CompanyLocation"/> uses for
/// <c>ApplicationUser.WorkLocationId</c>. That is what lets the channel column
/// land on an existing install without a NOT NULL backfill dance.</para>
/// </summary>
public class SalesChannel : BaseAuditableEntity, IActiveAware
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable short code used by integrations and order numbering (e.g. "DIRECT", "EBAY-US").</summary>
    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    public SalesChannelType ChannelType { get; set; } = SalesChannelType.DirectB2B;

    /// <summary>
    /// The account that carries the receivable for orders on this channel.
    ///
    /// <para>For <see cref="SalesChannelType.DirectB2B"/> this is null — the
    /// order's own customer is the sold-to. For retail and marketplace
    /// channels it is required: it is the house account ("Amazon US
    /// Marketplace", "Web Direct") that every order on the channel bills to,
    /// so AR, statements and accounting sync have a real counterparty rather
    /// than thousands of one-shot consumer rows.</para>
    /// </summary>
    public int? SoldToCustomerId { get; set; }
    public Customer? SoldToCustomer { get; set; }

    /// <summary>
    /// Default tax treatment for orders imported on this channel. Marketplace
    /// channels default to <see cref="TaxCollectedBy.Marketplace"/>; the
    /// per-order value still wins when an import supplies one, since a
    /// marketplace can broker both facilitator and seller-liable orders
    /// depending on the ship-to state.
    /// </summary>
    public TaxCollectedBy TaxCollectedBy { get; set; } = TaxCollectedBy.Seller;

    /// <summary>
    /// True for the single fallback channel. Orders with a null
    /// <see cref="SalesOrder.ChannelId"/> belong to it.
    /// </summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional prefix applied to generated order numbers on this channel, so
    /// marketplace orders are recognizable in a mixed list (e.g. "EB" →
    /// "EB-001042"). Null = use the install-wide numbering.
    /// </summary>
    public string? OrderNumberPrefix { get; set; }

    /// <summary>
    /// The credentials/polling row that feeds this channel, when it is fed by
    /// an integration rather than manual entry. Null for manual channels
    /// (walk-in, phone, trade show).
    /// </summary>
    public int? ECommerceIntegrationId { get; set; }
    public ECommerceIntegration? ECommerceIntegration { get; set; }

    // IActiveAware — blocks new orders against a retired channel while leaving
    // in-flight ones alone. Same contract Customer/Vendor/Part implement.
    public bool IsActiveForNewTransactions => IsActive;
    public string GetDisplayName() => string.IsNullOrWhiteSpace(Name) ? Code : Name;

    /// <summary>True when orders on this channel carry a consumer identity rather than an account.</summary>
    public bool IsRetail => ChannelType is SalesChannelType.DirectRetail or SalesChannelType.Marketplace;

    public ICollection<SalesOrder> SalesOrders { get; set; } = [];
    public ICollection<RetailBuyer> RetailBuyers { get; set; } = [];
    public ICollection<ChannelListing> Listings { get; set; } = [];
}
