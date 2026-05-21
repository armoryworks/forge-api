using Forge.Core.Enums;

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

    // F-027 — the single canonical invoice balance every consumer reads
    // (payment apply, status promotion, QBO payload). Per the domain DoD:
    //   balance = quantize(Total) − Σpayments − Σcredits
    //   • quantize = round half AWAY FROM ZERO at 2 dp (commercial/QBO rounding),
    //     NOT .NET's default ToEven/banker's — that diverges on half-cents and
    //     breaks INV-QBO3 cent-parity. The quantized Total makes the `== 0`
    //     PAID comparison exact (no epsilon, Ruling #7).
    //   • `credits` is a structural 0m placeholder; BE-4 owns the credit-memo
    //     entity, its sum, and its migration. The 3-term shape is locked in now
    //     so the formula is not re-derived when credits land.
    //   • No negative clamp (Delta 3): the 0 ≤ balance ≤ total bound is enforced
    //     at the application layer (F-026). A negative here is a surfaced INV-AR1
    //     violation, never masked with max(0, …).
    //   • Quantization lives HERE (compute-on-read) and not on Total/Subtotal,
    //     because those are projected server-side (e.g. SankeyReportRepository)
    //     where Math.Round(AwayFromZero) is not SQL-translatable. Full per-line
    //     rounding is tracked under GAP-INVOICE-05.
    public decimal BalanceDue
    {
        get
        {
            const decimal credits = 0m;
            return QuantizeMoney(Total) - AmountPaid - credits;
        }
    }

    private static decimal QuantizeMoney(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public Customer Customer { get; set; } = null!;
    public SalesOrder? SalesOrder { get; set; }
    public Shipment? Shipment { get; set; }
    public ICollection<InvoiceLine> Lines { get; set; } = [];
    public ICollection<PaymentApplication> PaymentApplications { get; set; } = [];
}
