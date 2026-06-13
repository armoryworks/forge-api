using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — one electronic submission of a payment to the bank channel (ACH / wire),
/// with automatic retry + manual-triage state. Generic over any transaction source via the polymorphic
/// (<see cref="SourceType"/>, <see cref="SourceId"/>) pair — mirrors the ActivityLog/StatusEntry
/// convention — wired concretely to <c>VendorPayment</c> today.
/// </summary>
public class PaymentTransmission : BaseAuditableEntity
{
    /// <summary>Polymorphic source discriminator (e.g. "VendorPayment").</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Id of the source row named by <see cref="SourceType"/>.</summary>
    public int SourceId { get; set; }

    public PaymentTransmissionStatus Status { get; set; } = PaymentTransmissionStatus.Queued;

    /// <summary>Number of submission attempts made so far (0 until the first attempt runs).</summary>
    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>When the next automatic retry is scheduled (null when terminal or not yet attempted).</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>Bank-issued reference returned on a successful submission.</summary>
    public string? SubmissionRef { get; set; }

    /// <summary>Snapshot of the source payment's amount for display/audit (source row may change).</summary>
    public decimal Amount { get; set; }

    /// <summary>Snapshot of the source payment's method (e.g. "BankTransfer", "Wire") for display/audit.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// User who initiated the source payment — the critical-notification target when the final attempt
    /// fails. Stamped from <c>AppDbContext.CurrentUserId</c> at creation (the source entities don't all
    /// carry a CreatedBy column). Null for system-initiated payments.
    /// </summary>
    public int? CreatedByUserId { get; set; }
}
