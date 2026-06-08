using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — the AP counterpart of <see cref="Invoice"/>. A bill owed to a
/// <see cref="Vendor"/>. Standalone mode: full CRUD. Phase-2 AP sub-ledger
/// (ACCOUNTING_SUITE_PLAN §6 Phase-2 / §7 "VendorBill"). When CAP-ACCT-FULLGL is on, approving a
/// bill posts the expense/AP journal inline (Dr line accounts / Cr AP control, party = vendor).
/// <para><c>PurchaseOrderId</c> is the (nullable) seam for the Phase-2 STAGE-D 3-way match
/// (PO ↔ receipt ↔ bill, GRNI clearing + PPV); standalone non-PO bills leave it null.</para>
/// </summary>
public class VendorBill : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version (mirrors Invoice.Version — WU-11 / F-026).</summary>
    public uint Version { get; set; }

    /// <summary>Our internal bill reference (e.g. BILL-1001), unique.</summary>
    public string BillNumber { get; set; } = string.Empty;

    public int VendorId { get; set; }

    // ── Multi-currency (Phase-4 FULLGL, additive). The currency the bill is
    // denominated in, and the booking rate (txn→functional) captured at creation.
    // Defaults: functional currency + rate 1, so single-currency installs are
    // byte-for-byte unchanged. The AP posting books the payable at this currency/
    // rate; settlement realizes FX vs the payment rate (mirror of Invoice).
    public int CurrencyId { get; set; }

    /// <summary>Booking FX rate (transaction→functional) captured at bill creation. Default 1.</summary>
    public decimal FxRate { get; set; } = 1m;

    /// <summary>The vendor's own invoice/document number (their reference), as printed on the bill.</summary>
    public string? VendorInvoiceNumber { get; set; }

    /// <summary>Optional link to the originating PO — the seam for STAGE-D 3-way match. Null for standalone bills.</summary>
    public int? PurchaseOrderId { get; set; }

    public VendorBillStatus Status { get; set; } = VendorBillStatus.Draft;
    public DateTimeOffset BillDate { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public CreditTerms? CreditTerms { get; set; }

    /// <summary>
    /// Tax charged by the vendor on this bill, as a stored amount (unlike sales-side tax, AP tax is a
    /// given figure on the vendor's document, not derived from our rate). Booked with the expense debit.
    /// </summary>
    public decimal TaxAmount { get; set; }

    public string? Notes { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public decimal Total => Subtotal + TaxAmount;
    public decimal AmountPaid => PaymentApplications.Sum(pa => pa.Amount);
    public decimal BalanceDue => Total - AmountPaid;

    public Vendor Vendor { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public PurchaseOrder? PurchaseOrder { get; set; }
    public ICollection<VendorBillLine> Lines { get; set; } = [];
    public ICollection<VendorPaymentApplication> PaymentApplications { get; set; } = [];
}
