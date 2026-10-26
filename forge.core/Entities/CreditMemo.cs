using Forge.Core.Enums;
using Forge.Core.Models.Accounting;

namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — a credit owed to a customer: a return, a price adjustment, goodwill or a write-off. Issued
/// memos post to the ledger, carry a negative AR open item, and are used up by applying them to invoices or by
/// refunding them. A memo linked to an invoice takes the invoice's currency and booking rate.
/// </summary>
public class CreditMemo : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned.</summary>
    public uint Version { get; set; }

    /// <summary>Human-readable number, <c>CM-000001</c>.</summary>
    public string CreditMemoNumber { get; set; } = string.Empty;

    public int CustomerId { get; set; }

    public int CurrencyId { get; set; }

    /// <summary>Booking rate (transaction to functional). The invoice's rate when the memo credits an invoice.</summary>
    public decimal FxRate { get; set; } = 1m;

    /// <summary>The invoice being credited, when there is one.</summary>
    public int? InvoiceId { get; set; }

    /// <summary>The customer return the memo was issued for, when there is one.</summary>
    public int? CustomerReturnId { get; set; }

    public CreditMemoReasonType ReasonType { get; set; } = CreditMemoReasonType.Return;

    public string Reason { get; set; } = string.Empty;

    public CreditMemoStatus Status { get; set; } = CreditMemoStatus.Draft;

    public DateTimeOffset CreditDate { get; set; }

    /// <summary>Sales tax rate credited on taxable lines. 0 for an exempt customer or a write-off.</summary>
    public decimal TaxRate { get; set; }

    public int? SalesTaxRateId { get; set; }

    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }

    public int? IssuedByUserId { get; set; }

    public DateTimeOffset? IssuedAt { get; set; }

    public DateTimeOffset? VoidedAt { get; set; }

    public string? VoidReason { get; set; }

    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Σ line amounts, restocking fees subtracted.</summary>
    public decimal Subtotal => Lines.Sum(l => l.SignedTotal);

    /// <summary>Σ taxable line amounts, restocking fees subtracted.</summary>
    public decimal TaxableSubtotal => Lines.Where(l => l.IsTaxable).Sum(l => l.SignedTotal);

    public decimal TaxAmount => Money.Round(TaxableSubtotal * TaxRate);

    public decimal Total => Money.Round(Subtotal) + TaxAmount;

    /// <summary>Σ applications to invoices that have not been reversed.</summary>
    public decimal AmountApplied => Applications.Where(a => a.ReversedAt == null).Sum(a => a.Amount);

    /// <summary>Σ recorded refunds.</summary>
    public decimal AmountRefunded => Refunds.Where(r => r.Status == CustomerRefundStatus.Recorded).Sum(r => r.Amount);

    /// <summary>What is left to apply or refund.</summary>
    public decimal OpenAmount => Status is CreditMemoStatus.Draft or CreditMemoStatus.Voided
        ? 0m
        : Total - AmountApplied - AmountRefunded;

    public bool IsWriteOff => ReasonType == CreditMemoReasonType.WriteOff;

    /// <summary>
    /// Sets <see cref="Status"/> from what is left open: Issued with nothing used, PartiallyApplied, Applied when used up
    /// by applications alone, Refunded when a refund took part of it. Draft and Voided memos are left alone.
    /// </summary>
    public void RecomputeStatus()
    {
        if (Status is CreditMemoStatus.Draft or CreditMemoStatus.Voided)
            return;

        var applied = AmountApplied;
        var refunded = AmountRefunded;
        Status = applied + refunded <= 0m
            ? CreditMemoStatus.Issued
            : Total - applied - refunded > 0m
                ? CreditMemoStatus.PartiallyApplied
                : refunded > 0m ? CreditMemoStatus.Refunded : CreditMemoStatus.Applied;
    }

    public Customer Customer { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public Invoice? Invoice { get; set; }
    public CustomerReturn? CustomerReturn { get; set; }
    public SalesTaxRate? SalesTaxRate { get; set; }
    public ICollection<CreditMemoLine> Lines { get; set; } = [];
    public ICollection<CreditMemoApplication> Applications { get; set; } = [];
    public ICollection<CustomerRefund> Refunds { get; set; } = [];
}
