using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// ⚡ BANKING BOUNDARY — one NACHA file's worth of vendor ACH credits (BANK-002 Phase A:
/// generate → download → upload to the bank portal by hand → release in-app). The generated
/// file text is stored on the row so the exact bytes uploaded can always be re-downloaded /
/// audited. Release is the SoD step: <see cref="ReleasedByUserId"/> must differ from
/// <c>CreatedBy</c>; releasing stamps the member payments as transmitted (submission
/// accepted — settlement is confirmed by BANK-001 statement reconciliation, never assumed).
/// A prenote batch (<see cref="IsPrenote"/>) carries zero-dollar entries that verify new
/// vendor bank accounts before live dollars flow.
/// </summary>
public class PaymentBatch : BaseAuditableEntity
{
    /// <summary>Our reference, e.g. ACH-00001 (also the NACHA batch's company entry description suffix).</summary>
    public string BatchNumber { get; set; } = string.Empty;

    public PaymentBatchStatus Status { get; set; } = PaymentBatchStatus.Draft;

    /// <summary>True for a zero-dollar prenote batch verifying new vendor bank accounts.</summary>
    public bool IsPrenote { get; set; }

    /// <summary>NACHA effective entry date — when the credits should settle.</summary>
    public DateOnly EffectiveEntryDate { get; set; }

    /// <summary>The generated NACHA file, exactly as downloadable (94-char lines, 10-record blocking).</summary>
    public string? FileContents { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }

    /// <summary>Who created/assembled the batch — the user release must differ from (SoD).</summary>
    public int CreatedByUserId { get; set; }

    public int? ReleasedByUserId { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }

    public decimal TotalAmount { get; set; }
    public int EntryCount { get; set; }

    public ICollection<PaymentBatchItem> Items { get; set; } = [];
}
