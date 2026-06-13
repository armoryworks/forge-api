using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — the AP counterpart of <see cref="Payment"/>: cash paid to a
/// <see cref="Vendor"/>, applied to one or more <see cref="VendorBill"/>s. Phase-2 AP sub-ledger.
/// When CAP-ACCT-FULLGL is on, creating a vendor payment posts inline: Dr AP control (party = vendor,
/// applied amount) / Cr Cash (mirror of the customer-payment cash receipt, opposite direction).
/// </summary>
public class VendorPayment : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version (mirrors Payment.Version — WU-11).</summary>
    public uint Version { get; set; }

    public string PaymentNumber { get; set; } = string.Empty;
    public int VendorId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset PaymentDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public decimal AppliedAmount => Applications.Sum(a => a.Amount);
    public decimal UnappliedAmount => Amount - AppliedAmount;

    public Vendor Vendor { get; set; } = null!;
    public ICollection<VendorPaymentApplication> Applications { get; set; } = [];
}
