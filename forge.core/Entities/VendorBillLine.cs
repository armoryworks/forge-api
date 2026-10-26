using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;

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
    /// Item (default) or Freight. A freight line has no part and no PO line; it debits Freight Clearing for
    /// <see cref="FreightForPurchaseOrderId"/> up to that PO's open accrual and Freight-In beyond it, or its own
    /// key / account when it names no PO.
    /// </summary>
    public DocumentLineType LineType { get; set; } = DocumentLineType.Item;

    /// <summary>The purchase order whose receipt freight accrual this freight line clears. Any vendor may bill it.</summary>
    public int? FreightForPurchaseOrderId { get; set; }

    /// <summary>
    /// The functional amount this freight line cleared from Freight Clearing when the bill posted; null until
    /// it posts, and on lines that clear nothing. The next freight bill for the same PO clears only what is left.
    /// </summary>
    public decimal? FreightClearedAmount { get; set; }

    /// <summary>
    /// Optional job tag carried onto the GL debit line for job costing (mirrors
    /// <see cref="Expense.JobId"/> — set when the bill line originates from a job-costed
    /// expense or any other job-attributable purchase). Standalone-bill posting only;
    /// PO-matched lines clear GRNI, which is never job-tagged.
    /// </summary>
    public int? JobId { get; set; }

    /// <summary>
    /// GL account-determination key this line debits (e.g. OPERATING_EXPENSE, INVENTORY_RAW, GRNI).
    /// Default OPERATING_EXPENSE. Resolved per-book via AccountDeterminationRule at posting time.
    /// </summary>
    public string AccountDeterminationKey { get; set; } = "OPERATING_EXPENSE";

    /// <summary>
    /// Optional explicit GL account this line debits, chosen on a standalone bill when no determination key
    /// fits (e.g. an expense account the company added to its chart). When set it wins over
    /// <see cref="AccountDeterminationKey"/>; PO-matched lines always clear GRNI and never carry one.
    /// </summary>
    public int? GlAccountId { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;

    public bool IsFreight => LineType == DocumentLineType.Freight;

    public VendorBill VendorBill { get; set; } = null!;
    public Part? Part { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
    public PurchaseOrder? FreightForPurchaseOrder { get; set; }
    public Job? Job { get; set; }
    public GlAccount? GlAccount { get; set; }
}
