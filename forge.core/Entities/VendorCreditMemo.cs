using Forge.Core.Enums;
using Forge.Core.Models.Accounting;

namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — the AP counterpart of <see cref="CreditMemo"/>: an amount a vendor owes back to us, either a
/// credit memo the vendor sent or a debit memo we raised. Issued memos post to the ledger, carry a negative AP open
/// item, and are used up by applying them to open bills of the same vendor and currency. A memo linked to a bill takes
/// the bill's currency and booking rate; a memo for a return to vendor clears the return's clearing account.
/// </summary>
public class VendorCreditMemo : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned.</summary>
    public uint Version { get; set; }

    /// <summary>Human-readable number, <c>VCM-000001</c>.</summary>
    public string MemoNumber { get; set; } = string.Empty;

    public VendorCreditMemoKind Kind { get; set; } = VendorCreditMemoKind.VendorCredit;

    public int VendorId { get; set; }

    public int CurrencyId { get; set; }

    /// <summary>Booking rate (transaction to functional). The bill's rate when the memo credits a bill.</summary>
    public decimal FxRate { get; set; } = 1m;

    /// <summary>The bill being credited, when there is one.</summary>
    public int? VendorBillId { get; set; }

    /// <summary>The return to vendor the memo credits, when there is one.</summary>
    public int? VendorReturnId { get; set; }

    public VendorCreditMemoSourceType SourceType { get; set; } = VendorCreditMemoSourceType.Other;

    public string Reason { get; set; } = string.Empty;

    /// <summary>The vendor's own credit memo number, as printed on their document.</summary>
    public string? VendorReference { get; set; }

    public VendorCreditMemoStatus Status { get; set; } = VendorCreditMemoStatus.Draft;

    public DateTimeOffset MemoDate { get; set; }

    /// <summary>Purchase tax credited back, as a stored amount (like <see cref="VendorBill.TaxAmount"/>).</summary>
    public decimal TaxAmount { get; set; }

    public string? Notes { get; set; }

    /// <summary>The issue journal entry, when the memo posted to the ledger.</summary>
    public long? JournalEntryId { get; set; }

    public int? CreatedByUserId { get; set; }

    public int? IssuedByUserId { get; set; }

    public DateTimeOffset? IssuedAt { get; set; }

    public DateTimeOffset? VoidedAt { get; set; }

    public string? VoidReason { get; set; }

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);

    /// <summary>The credit, rounded to the cent: exactly what the issue posting debits to AP.</summary>
    public decimal Total => Money.Round(Subtotal + TaxAmount);

    /// <summary>Σ applications to bills that have not been reversed.</summary>
    public decimal AmountApplied => Applications.Where(a => a.ReversedAt == null).Sum(a => a.Amount);

    /// <summary>What is left to apply.</summary>
    public decimal OpenAmount => Status is VendorCreditMemoStatus.Draft or VendorCreditMemoStatus.Voided
        ? 0m
        : Total - AmountApplied;

    /// <summary>
    /// Sets <see cref="Status"/> from what is left open: Open with nothing applied, PartiallyApplied, Applied when used
    /// up. Draft and Voided memos are left alone.
    /// </summary>
    public void RecomputeStatus()
    {
        if (Status is VendorCreditMemoStatus.Draft or VendorCreditMemoStatus.Voided)
            return;

        var applied = AmountApplied;
        Status = applied <= 0m
            ? VendorCreditMemoStatus.Open
            : Total - applied > 0m ? VendorCreditMemoStatus.PartiallyApplied : VendorCreditMemoStatus.Applied;
    }

    public Vendor Vendor { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public VendorBill? VendorBill { get; set; }
    public VendorReturn? VendorReturn { get; set; }
    public ICollection<VendorCreditMemoLine> Lines { get; set; } = [];
    public ICollection<VendorCreditMemoApplication> Applications { get; set; } = [];
}
