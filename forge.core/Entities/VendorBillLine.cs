namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — a line on a <see cref="VendorBill"/> (the AP counterpart of
/// <see cref="InvoiceLine"/>). Each line carries the GL account-determination <b>key</b> it debits
/// (not a hardcoded account number — §5.1); the AP posting service resolves the key against the
/// book's <c>AccountDeterminationRule</c>s. Default <see cref="AccountDeterminationKey"/> =
/// <c>OPERATING_EXPENSE</c>; inventory/PO-matched bills (STAGE D) use <c>GRNI</c>/<c>INVENTORY_*</c>.
/// </summary>
public class VendorBillLine : BaseEntity
{
    public int VendorBillId { get; set; }

    /// <summary>Optional part reference (inventory/PO bills); null for service/expense lines.</summary>
    public int? PartId { get; set; }

    /// <summary>Optional link to the matched PO line (STAGE-D 3-way match) — supplies the PO price the
    /// GRNI accrual was booked at, and the open (received-not-billed) quantity. Null for standalone bills.</summary>
    public int? PurchaseOrderLineId { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int LineNumber { get; set; }

    /// <summary>
    /// GL account-determination key this line debits (e.g. OPERATING_EXPENSE, INVENTORY_RAW, GRNI).
    /// Default OPERATING_EXPENSE. Resolved per-book via AccountDeterminationRule at posting time.
    /// </summary>
    public string AccountDeterminationKey { get; set; } = "OPERATING_EXPENSE";

    public decimal LineTotal => Quantity * UnitPrice;

    public VendorBill VendorBill { get; set; } = null!;
    public Part? Part { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
}
