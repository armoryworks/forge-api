using Forge.Core.Enums;
using Forge.Core.Models.Accounting;

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

    // ── Multi-currency (Phase-4 FULLGL, additive). The currency the invoice is
    // denominated in, and the booking rate (txn→functional) captured at creation.
    // Defaults: functional currency + rate 1, so single-currency installs are
    // byte-for-byte unchanged. The AR posting books revenue at this currency/rate
    // (FunctionalAmount = txn × FxRate); settlement realizes FX vs the payment rate.
    public int CurrencyId { get; set; }

    /// <summary>Booking FX rate (transaction→functional) captured at invoice creation. Default 1.</summary>
    public decimal FxRate { get; set; } = 1m;

    public int? SalesOrderId { get; set; }
    public int? ShipmentId { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTimeOffset InvoiceDate { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public CreditTerms? CreditTerms { get; set; }

    /// <summary>The payment terms the due date and discount came from. Null on invoices from before payment terms.</summary>
    public int? PaymentTermsId { get; set; }

    /// <summary>Early-payment discount in percent, copied from the terms when the invoice was created. 0 = none.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>The last day the early-payment discount may be taken, copied from the terms. Null = no discount.</summary>
    public DateTimeOffset? DiscountDueDate { get; set; }
    public decimal TaxRate { get; set; }

    /// <summary>Why <see cref="TaxRate"/> differs from the customer's computed rate. Null = the computed rate applies.</summary>
    public TaxOverrideReason? TaxOverrideReason { get; set; }

    /// <summary>The note recorded with <see cref="TaxOverrideReason"/>.</summary>
    public string? TaxOverrideNote { get; set; }

    /// <summary>The sales-tax jurisdiction <see cref="TaxRate"/> came from, when known. Decides whether freight starts taxable.</summary>
    public int? SalesTaxRateId { get; set; }
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
    public decimal FreightSubtotal => Lines.Where(l => l.IsFreight).Sum(l => l.LineTotal);
    public decimal TaxableSubtotal => Lines.Where(l => l.IsTaxable).Sum(l => l.LineTotal);
    public decimal TaxAmount => QuantizeMoney(TaxableSubtotal * TaxRate);
    public decimal Total => Subtotal + TaxAmount;
    public decimal AmountPaid => PaymentApplications.Sum(pa => pa.Amount);

    /// <summary>Early-payment discounts taken by the payments applied to this invoice.</summary>
    public decimal DiscountsTaken => PaymentApplications.Sum(pa => pa.DiscountAmount);

    /// <summary>Credit memo amounts applied to this invoice and not unapplied.</summary>
    public decimal CreditsApplied => CreditApplications.Where(a => a.ReversedAt == null).Sum(a => a.Amount);

    // F-027 — the single canonical invoice balance every consumer reads
    // (payment apply, status promotion, QBO payload). Per the domain DoD:
    //   balance = quantize(Total) − Σpayments − Σdiscounts − Σcredits
    //   • quantize = round half AWAY FROM ZERO at 2 dp (commercial/QBO rounding),
    //     NOT .NET's default ToEven/banker's — that diverges on half-cents and
    //     breaks INV-QBO3 cent-parity. The quantized Total makes the `== 0`
    //     PAID comparison exact (no epsilon, Ruling #7).
    //   • credits = live credit memo applications (CreditsApplied).
    //   • No negative clamp (Delta 3): the 0 ≤ balance ≤ total bound is enforced
    //     at the application layer (F-026). A negative here is a surfaced INV-AR1
    //     violation, never masked with max(0, …).
    //   • Quantization lives HERE (compute-on-read) and not on Total/Subtotal,
    //     because those are projected server-side (e.g. SankeyReportRepository)
    //     where Math.Round(AwayFromZero) is not SQL-translatable. Full per-line
    //     rounding is tracked under GAP-INVOICE-05.
    public decimal BalanceDue => QuantizeMoney(Total) - AmountPaid - DiscountsTaken - CreditsApplied;

    private static decimal QuantizeMoney(decimal value)
        => Money.Round(value);

    public Customer Customer { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public SalesOrder? SalesOrder { get; set; }
    public Shipment? Shipment { get; set; }
    public SalesTaxRate? SalesTaxRate { get; set; }
    public ICollection<InvoiceLine> Lines { get; set; } = [];
    public ICollection<PaymentApplication> PaymentApplications { get; set; } = [];
    public ICollection<CreditMemoApplication> CreditApplications { get; set; } = [];
}
