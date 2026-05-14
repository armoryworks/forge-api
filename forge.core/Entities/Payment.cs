
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

    public Customer Customer { get; set; } = null!;
    public ICollection<PaymentApplication> Applications { get; set; } = [];
}
