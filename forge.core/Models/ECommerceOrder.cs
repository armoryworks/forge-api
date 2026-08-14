using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// One order as reported by a storefront or marketplace, normalised across
/// platforms. This is the connector contract: a poller's only job is to turn
/// whatever the platform returns into this shape, and the import handler turns
/// this into a <see cref="Entities.SalesOrder"/>.
/// </summary>
public record ECommerceOrder
{
    /// <summary>The platform's internal order id.</summary>
    public string ExternalId { get; init; } = string.Empty;

    /// <summary>The human-facing order number the buyer sees and quotes back to support.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>
    /// The platform's stable identifier for the buyer. Distinct from
    /// <see cref="CustomerEmail"/>, which on marketplaces is a rotating relay
    /// address and must never be used as an identity key.
    /// </summary>
    public string? BuyerId { get; init; }

    public string CustomerEmail { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string? CustomerPhone { get; init; }

    /// <summary>True only when the platform explicitly reports a marketing opt-in.</summary>
    public bool MarketingConsent { get; init; }

    public IReadOnlyList<ECommerceOrderLine> Lines { get; init; } = [];

    public ECommerceAddress ShippingAddress { get; init; } = new();

    /// <summary>Null when the platform does not distinguish it from the shipping address.</summary>
    public ECommerceAddress? BillingAddress { get; init; }

    /// <summary>Item subtotal before tax, shipping and order-level discount.</summary>
    public decimal SubtotalAmount { get; init; }

    /// <summary>Shipping charged to the buyer. Revenue — imported as its own order line.</summary>
    public decimal ShippingAmount { get; init; }

    /// <summary>Order-level discount, as a positive number.</summary>
    public decimal DiscountAmount { get; init; }

    /// <summary>Sales tax charged to the buyer. See <see cref="TaxCollectedBy"/> for whether we owe it.</summary>
    public decimal TaxAmount { get; init; }

    /// <summary>Grand total the buyer paid.</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>
    /// Who is liable for <see cref="TaxAmount"/>. Null lets the channel's
    /// default apply; connectors should set it explicitly when the platform
    /// reports facilitator status per order, which is the accurate case —
    /// a marketplace can be facilitator in one state and not another.
    /// </summary>
    public TaxCollectedBy? TaxCollectedBy { get; init; }

    /// <summary>Fees the platform reported against this order at order time. Informational — settlement is authoritative.</summary>
    public decimal? PlatformFeeAmount { get; init; }

    public string CurrencyCode { get; init; } = "USD";

    public DateTimeOffset OrderDate { get; init; }

    /// <summary>The platform's own status string, kept verbatim for triage.</summary>
    public string? PlatformStatus { get; init; }

    /// <summary>Buyer-visible notes or gift messages, when the platform exposes them.</summary>
    public string? Notes { get; init; }
}
