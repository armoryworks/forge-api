namespace Forge.Core.Models;

/// <summary>
/// Read model for one payment milestone. <paramref name="Status"/> is the
/// COMPUTED effective status (persisted Pending is promoted to Due when the
/// milestone's trigger condition holds against the linked documents' state);
/// <paramref name="AmountDue"/> is the locked amount when set, otherwise
/// percentage × live document total quantized at 2 dp away-from-zero.
/// </summary>
public record PaymentMilestoneResponseModel(
    int Id,
    int Sequence,
    string Name,
    decimal Percentage,
    string DueTrigger,
    DateTimeOffset? DueDate,
    int? NetDays,
    string Status,
    decimal AmountDue,
    decimal PaidAmount,
    int? InvoiceId,
    string? Notes);
