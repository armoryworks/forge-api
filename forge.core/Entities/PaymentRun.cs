using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — a persisted payment run: the bills picked to pay on one date by one method, paid one vendor
/// payment per vendor and currency. Every item keeps its own status, so a run that fails part-way is Interrupted
/// rather than silently half-done, and resuming it pays only the items not yet paid. Open vendor credits picked as
/// deductions are applied with each vendor's payment.
/// </summary>
public class PaymentRun : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned.</summary>
    public uint Version { get; set; }

    /// <summary>Human-readable number, <c>PR-000001</c>.</summary>
    public string RunNumber { get; set; } = string.Empty;

    public PaymentRunStatus Status { get; set; } = PaymentRunStatus.Pending;

    public DateTimeOffset PaymentDate { get; set; }

    /// <summary>The payment method name (<see cref="PaymentMethod"/>).</summary>
    public string Method { get; set; } = string.Empty;

    public string? ReferenceNumber { get; set; }

    public string? Notes { get; set; }

    /// <summary>Whether each vendor's open credit memos are deducted from its payment.</summary>
    public bool ApplyVendorCredits { get; set; } = true;

    public int? CreatedByUserId { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Why the run was interrupted, when it was.</summary>
    public string? LastError { get; set; }

    public ICollection<PaymentRunItem> Items { get; set; } = [];
    public ICollection<PaymentRunCredit> Credits { get; set; } = [];
}
