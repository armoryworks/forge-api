
namespace Forge.Core.Entities;

public class SalesOrder : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned. WU-11.</summary>
    public uint Version { get; set; }

    public string OrderNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int? QuoteId { get; set; }
    public int? ShippingAddressId { get; set; }
    public int? BillingAddressId { get; set; }
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
    public CreditTerms? CreditTerms { get; set; }
    public DateTimeOffset? ConfirmedDate { get; set; }
    public DateTimeOffset? RequestedDeliveryDate { get; set; }
    public string? CustomerPO { get; set; }
    public string? Notes { get; set; }
    public decimal TaxRate { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public decimal TaxAmount => Subtotal * TaxRate;
    public decimal Total => Subtotal + TaxAmount;

    public Customer Customer { get; set; } = null!;
    public Quote? Quote { get; set; }
    public CustomerAddress? ShippingAddress { get; set; }
    public CustomerAddress? BillingAddress { get; set; }
    public ICollection<SalesOrderLine> Lines { get; set; } = [];
    public ICollection<Shipment> Shipments { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
}
