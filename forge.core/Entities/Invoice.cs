
namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — Standalone mode: full CRUD. Integrated mode: read-only cache from accounting system.
/// </summary>
public class Invoice : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned. WU-11.</summary>
    public uint Version { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int? SalesOrderId { get; set; }
    public int? ShipmentId { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTimeOffset InvoiceDate { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public CreditTerms? CreditTerms { get; set; }
    public decimal TaxRate { get; set; }
    public string? Notes { get; set; }

    // Customer PO reference — copied from SalesOrder.CustomerPO when the
    // invoice is generated from an SO. Many B2B customers won't process an
    // invoice that doesn't echo their own PO number.
    public string? CustomerPO { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public decimal TaxAmount => Subtotal * TaxRate;
    public decimal Total => Subtotal + TaxAmount;
    public decimal AmountPaid => PaymentApplications.Sum(pa => pa.Amount);
    public decimal BalanceDue => Total - AmountPaid;

    public Customer Customer { get; set; } = null!;
    public SalesOrder? SalesOrder { get; set; }
    public Shipment? Shipment { get; set; }
    public ICollection<InvoiceLine> Lines { get; set; } = [];
    public ICollection<PaymentApplication> PaymentApplications { get; set; } = [];
}
