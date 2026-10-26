using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — Standalone mode: full CRUD. Integrated mode: read-only cache from accounting system.
/// </summary>
public class Payment : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned. WU-11.</summary>
    public uint Version { get; set; }

    public string PaymentNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Currency the payment is denominated in. Null on rows from before multi-currency = the functional currency.</summary>
    public int? CurrencyId { get; set; }

    /// <summary>Rate from <see cref="CurrencyId"/> to the functional currency, captured on the payment date. 1 for the functional currency.</summary>
    public decimal FxRate { get; set; } = 1m;
    public DateTimeOffset PaymentDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public decimal AppliedAmount => Applications.Sum(a => a.Amount);

    /// <summary>Unapplied cash paid back to the customer by recorded refunds.</summary>
    public decimal RefundedAmount => Refunds.Where(r => r.Status == CustomerRefundStatus.Recorded).Sum(r => r.Amount);

    public decimal UnappliedAmount => Amount - AppliedAmount - RefundedAmount;

    public Customer Customer { get; set; } = null!;
    public Currency? Currency { get; set; }
    public ICollection<PaymentApplication> Applications { get; set; } = [];
    public ICollection<CustomerRefund> Refunds { get; set; } = [];
}
