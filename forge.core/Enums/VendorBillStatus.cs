namespace Forge.Core.Enums;

/// <summary>
/// Lifecycle of a vendor bill (the AP counterpart of <c>InvoiceStatus</c>).
/// Phase-2 AP sub-ledger. AP posting fires on the <see cref="Approved"/> transition
/// (mirrors invoice finalize → AR posting); <see cref="PartiallyPaid"/>/<see cref="Paid"/>
/// are driven by vendor-payment applications; <see cref="Void"/> is a non-posting
/// cancel of an unposted/never-approved bill.
/// </summary>
public enum VendorBillStatus
{
    /// <summary>Entered, not yet approved — no GL impact.</summary>
    Draft,

    /// <summary>Approved for payment — the AP / expense journal posts on this transition.</summary>
    Approved,

    /// <summary>Approved and partially settled by one or more vendor payments.</summary>
    PartiallyPaid,

    /// <summary>Fully settled.</summary>
    Paid,

    /// <summary>Cancelled before approval/posting (a posted bill is corrected via reversal, never voided).</summary>
    Void,
}
