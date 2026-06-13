using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class Expense : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int? JobId { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ReceiptFileId { get; set; }
    public ExpenseStatus Status { get; set; } = ExpenseStatus.Pending;
    public int? ApprovedBy { get; set; }
    public string? ApprovalNotes { get; set; }
    public string? ExternalExpenseId { get; set; }

    // Standardized accounting integration fields
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }

    public DateTimeOffset ExpenseDate { get; set; }

    // ── Phase-1 STAGE C — settlement disambiguation for GL posting ───────────
    // Additive, operationally inert (no behavior outside the dark GL path, §6
    // Phase-1 row). When the expense is approved AND CAP-ACCT-FULLGL is on, the
    // posting service books Dr Expense / Cr AP (party = vendor) when this expense
    // settles to a vendor, else Dr Expense / Cr Cash (§7 matrix "Expense
    // approved"). The pair below disambiguates which leg the credit takes.

    /// <summary>
    /// How this expense settles for GL purposes (§7 "Expense approved"). When
    /// null, the posting service infers: a present <see cref="VendorId"/> ⇒
    /// Accounts Payable; otherwise Cash. Inert until CAP-ACCT-FULLGL is enabled.
    /// </summary>
    public ExpenseSettlementTarget? SettlementTarget { get; set; }

    /// <summary>
    /// Optional vendor the expense is owed to. When set, the AP credit leg
    /// carries this vendor as the sub-ledger party (AP is a control account, so
    /// the posting engine requires a party on that line — §5.2). Null for
    /// out-of-pocket / cash expenses.
    /// </summary>
    public int? VendorId { get; set; }

    public Job? Job { get; set; }
    public Vendor? Vendor { get; set; }
}
