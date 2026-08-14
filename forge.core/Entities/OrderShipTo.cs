namespace Forge.Core.Entities;

/// <summary>
/// A frozen ship-to address captured on a single <see cref="SalesOrder"/>.
///
/// <para><b>Why this is not a CustomerAddress.</b> Both
/// <c>sales_orders.shipping_address_id</c> and <c>shipments.shipping_address_id</c>
/// are foreign keys into <c>customer_addresses</c>, whose rows belong to a
/// <see cref="Customer"/>. A retail order's ship-to belongs to a consumer, not
/// to the house account the order bills against — routing it through
/// <see cref="CustomerAddress"/> would pile every consumer address in the
/// install under one customer and surface them in that customer's address
/// picker.</para>
///
/// <para><b>Why per-order rather than per-buyer.</b> A marketplace buyer can
/// ship to a different address on every order (gifts, work vs home), and the
/// address that was shipped to must not change retroactively when they later
/// update their profile. This is a snapshot, taken at import, never edited by a
/// later sync.</para>
///
/// <para>Resolution order for anything that needs a destination is
/// <c>Shipment.ShippingAddress ?? SalesOrder.ShippingAddress ?? SalesOrder.ShipTo</c>.</para>
/// </summary>
public class OrderShipTo : BaseAuditableEntity
{
    public int SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "US";

    /// <summary>Contact phone for the carrier, when the channel supplies one.</summary>
    public string? Phone { get; set; }

    /// <summary>
    /// True once an address-validation pass (see <c>IAddressValidationService</c>)
    /// confirmed deliverability. Marketplace addresses arrive pre-validated by
    /// the platform, so imports may set this without a second USPS round trip.
    /// </summary>
    public bool IsValidated { get; set; }

    public string ToSingleLine() =>
        string.Join(", ", new[] { Line1, Line2, City, State, PostalCode, Country }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
}
