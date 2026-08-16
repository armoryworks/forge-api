using System.ComponentModel.DataAnnotations.Schema;

using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class SalesOrder : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned. WU-11.</summary>
    public uint Version { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// The account that owes the money. Always set. On a retail or marketplace
    /// channel this is the channel's house account (see
    /// <see cref="SalesChannel.SoldToCustomerId"/>) rather than the person who
    /// bought — that consumer is <see cref="RetailBuyerId"/>. Keeping this
    /// non-nullable is what lets AR aging, statements, credit and accounting
    /// sync stay untouched by the retail work.
    /// </summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// Route to market. Null resolves to the install's default channel — the
    /// same "null = the default row" convention <c>ApplicationUser.WorkLocationId</c>
    /// uses — so the column could land on existing installs without a NOT NULL
    /// backfill. New orders always set it explicitly.
    /// </summary>
    public int? ChannelId { get; set; }
    public SalesChannel? Channel { get; set; }

    /// <summary>
    /// The consumer this order is for, on retail and marketplace channels.
    /// Null on B2B orders, where <see cref="Customer"/> is already the buyer.
    /// </summary>
    public int? RetailBuyerId { get; set; }
    public RetailBuyer? RetailBuyer { get; set; }

    /// <summary>
    /// Who collected the sales tax on this order. <see cref="Enums.TaxCollectedBy.Marketplace"/>
    /// makes <see cref="TaxAmount"/> a pass-through that never reaches the
    /// install's sales-tax liability. Defaults to <see cref="Enums.TaxCollectedBy.Seller"/>,
    /// which is the pre-channel behaviour for every existing row.
    /// </summary>
    public TaxCollectedBy TaxCollectedBy { get; set; } = TaxCollectedBy.Seller;

    /// <summary>
    /// The channel's own order number (marketplace order id). On retail orders
    /// this is what <see cref="CustomerPO"/> is for a B2B order — the buyer's
    /// reference, echoed on documents and used to find the order from a
    /// customer-service query.
    /// </summary>
    public string? ExternalOrderNumber { get; set; }

    /// <summary>
    /// Frozen consumer ship-to. Populated on retail orders where the
    /// destination is not a <see cref="CustomerAddress"/> under the house
    /// account. See <see cref="OrderShipTo"/> for why this is not reusing the
    /// customer address table.
    /// </summary>
    public OrderShipTo? ShipTo { get; set; }

    /// <summary>
    /// The single attestation that authorizes this order — the one rendered on
    /// the Authorized-by line. Distinct from the one-to-many acceptance history:
    /// several statements may exist (a superseded PO, the MSA behind it), and
    /// this points at the one currently in force.
    /// </summary>
    public int? AuthorizingAttestationId { get; set; }
    public Attestation? AuthorizingAttestation { get; set; }

    public int? QuoteId { get; set; }
    public int? ShippingAddressId { get; set; }
    public int? BillingAddressId { get; set; }
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
    public CreditTerms? CreditTerms { get; set; }
    public DateTimeOffset? ConfirmedDate { get; set; }
    public DateTimeOffset? RequestedDeliveryDate { get; set; }

    /// <summary>Fee charged when this order was cancelled late (null = no fee). Billed via a fee invoice.</summary>
    public decimal? CancellationFeeAmount { get; set; }
    public string? CancellationFeeReason { get; set; }

    public string? CustomerPO { get; set; }
    public string? Notes { get; set; }
    public decimal TaxRate { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }

    /// <summary>
    /// Addendum orders — post-lock changes to an accepted order are captured as
    /// a new Draft SO linked to the original (delta lines only), never by
    /// editing the locked record. Numbered {parent}-A{n}.
    /// </summary>
    public int? ParentSalesOrderId { get; set; }
    public int? AddendumNumber { get; set; }

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public decimal TaxAmount => Subtotal * TaxRate;
    public decimal Total => Subtotal + TaxAmount;

    /// <summary>
    /// The portion of <see cref="TaxAmount"/> the install actually owes a
    /// taxing authority. Zero when a marketplace facilitator collected and
    /// remits it — the buyer still paid the tax and it still belongs on the
    /// document total, but it is not the install's payable. Anything computing
    /// sales-tax liability must read this, never <see cref="TaxAmount"/>.
    /// </summary>
    public decimal SellerTaxLiability =>
        TaxCollectedBy == TaxCollectedBy.Marketplace ? 0m : TaxAmount;

    public Customer Customer { get; set; } = null!;
    public Quote? Quote { get; set; }
    [ForeignKey(nameof(ParentSalesOrderId))]
    public SalesOrder? ParentSalesOrder { get; set; }
    [InverseProperty(nameof(ParentSalesOrder))]
    public ICollection<SalesOrder> Addenda { get; set; } = [];
    public CustomerAddress? ShippingAddress { get; set; }
    public CustomerAddress? BillingAddress { get; set; }
    public ICollection<SalesOrderLine> Lines { get; set; } = [];
    public ICollection<Shipment> Shipments { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
}
