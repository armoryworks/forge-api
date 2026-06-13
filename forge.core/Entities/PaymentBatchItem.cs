namespace Forge.Core.Entities;

/// <summary>
/// ⚡ BANKING BOUNDARY — one entry-detail line of a <see cref="PaymentBatch"/>: a vendor payment
/// (or a zero-dollar prenote when the batch <c>IsPrenote</c>) destined for one vendor bank
/// account. <see cref="TraceNumber"/> is the NACHA trace assigned at generation — the join key
/// back from bank returns/NOC files (Phase C).
/// </summary>
public class PaymentBatchItem : BaseEntity
{
    public int PaymentBatchId { get; set; }

    /// <summary>Null on prenote items (zero-dollar account verification, no payment).</summary>
    public int? VendorPaymentId { get; set; }

    public int VendorBankAccountId { get; set; }

    /// <summary>Credit amount; 0 for prenote items.</summary>
    public decimal Amount { get; set; }

    /// <summary>NACHA trace number (ODFI 8 digits + 7-digit sequence), assigned at file generation.</summary>
    public string? TraceNumber { get; set; }

    public PaymentBatch PaymentBatch { get; set; } = null!;
    public VendorPayment? VendorPayment { get; set; }
    public VendorBankAccount VendorBankAccount { get; set; } = null!;
}
