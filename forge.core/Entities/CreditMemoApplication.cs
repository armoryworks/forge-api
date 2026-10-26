namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — part of a credit memo used against an invoice, in the shared document currency. Unapplying
/// stamps <see cref="ReversedAt"/>; the row stays for the audit trail.
/// </summary>
public class CreditMemoApplication : BaseEntity
{
    public int CreditMemoId { get; set; }

    public int InvoiceId { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset AppliedAt { get; set; }

    public int? AppliedByUserId { get; set; }

    public DateTimeOffset? ReversedAt { get; set; }

    public int? ReversedByUserId { get; set; }

    public bool IsLive => ReversedAt is null;

    public CreditMemo CreditMemo { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;
}
