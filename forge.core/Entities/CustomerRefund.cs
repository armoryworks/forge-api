using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — money paid back to a customer, either the open part of a credit memo or unapplied cash
/// from a payment. Exactly one of <see cref="CreditMemoId"/> and <see cref="PaymentId"/> is set.
/// </summary>
public class CustomerRefund : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned.</summary>
    public uint Version { get; set; }

    /// <summary>Human-readable number, <c>RF-000001</c>.</summary>
    public string RefundNumber { get; set; } = string.Empty;

    public int CustomerId { get; set; }

    public int CurrencyId { get; set; }

    /// <summary>Rate the cash went out at (transaction to functional).</summary>
    public decimal FxRate { get; set; } = 1m;

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; }

    public DateTimeOffset RefundDate { get; set; }

    public string? Reference { get; set; }

    public string? Notes { get; set; }

    public int? CreditMemoId { get; set; }

    public int? PaymentId { get; set; }

    public CustomerRefundStatus Status { get; set; } = CustomerRefundStatus.Recorded;

    public DateTimeOffset? VoidedAt { get; set; }

    public string? VoidReason { get; set; }

    public int? CreatedByUserId { get; set; }

    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public Customer Customer { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public CreditMemo? CreditMemo { get; set; }
    public Payment? Payment { get; set; }
}
