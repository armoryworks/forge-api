using Forge.Core.Enums;
using Forge.Core.Models.Accounting;

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

    /// <summary>
    /// Optional link to the vendor-settled <see cref="Expense"/> this bill was promoted from
    /// (expense approval auto-creates the bill so the payable flows through the one AP pipeline —
    /// open item, aging, vendor payments, void. At most one NON-VOID bill per expense; a demoted
    /// (voided) bill may be superseded by a fresh promotion on re-approval.
    /// </summary>
    public int? ExpenseId { get; set; }

    /// <summary>
    /// The user who entered the bill. Approval refuses this user (segregation of duties). Null for bills
    /// created by the system (expense promotion, conversion) or recorded before the column existed.
    /// </summary>
    public int? CreatedByUserId { get; set; }

    public VendorBillStatus Status { get; set; } = VendorBillStatus.Draft;
    public DateTimeOffset BillDate { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public CreditTerms? CreditTerms { get; set; }

    /// <summary>The payment terms the due date and discount came from. Null on bills from before payment terms.</summary>
    public int? PaymentTermsId { get; set; }

    /// <summary>Early-payment discount in percent, copied from the terms when the bill was created. 0 = none.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>The last day the early-payment discount may be taken, copied from the terms. Null = no discount.</summary>
    public DateTimeOffset? DiscountDueDate { get; set; }

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

    /// <summary>
    /// The payable, rounded to the cent (half away from zero): exactly what the AP posting credits and the AP
    /// open item carries. Never projected server-side, so the rounding can live on the property itself.
    /// </summary>
    public decimal Total => Money.Round(Subtotal + TaxAmount);

    public decimal AmountPaid => PaymentApplications.Sum(pa => pa.Amount);

    /// <summary>Early-payment discounts taken by the payments applied to this bill.</summary>
    public decimal DiscountsTaken => PaymentApplications.Sum(pa => pa.DiscountAmount);

    /// <summary>Vendor credit memo amounts applied to this bill and not unapplied.</summary>
    public decimal CreditsApplied => CreditApplications.Where(a => a.ReversedAt == null).Sum(a => a.Amount);

    /// <summary>
    /// What is still owed, quantized to the cent (the AP mirror of <see cref="Invoice.BalanceDue"/>). The raw
    /// difference is rounded, not the total alone, so a legacy bill paid at its raw sub-cent total reads 0.
    /// </summary>
    public decimal BalanceDue => Money.Round(Total - AmountPaid - DiscountsTaken - CreditsApplied);

    public Vendor Vendor { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Expense? Expense { get; set; }
    public ICollection<VendorBillLine> Lines { get; set; } = [];
    public ICollection<VendorPaymentApplication> PaymentApplications { get; set; } = [];
    public ICollection<VendorCreditMemoApplication> CreditApplications { get; set; } = [];
}
