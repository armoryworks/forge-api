using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// One milestone in a PUT-style payment-schedule upsert. Percentages are the
/// source of truth (Σ across the schedule must equal 100); amounts derive at
/// read time from the live document total.
/// </summary>
public record PaymentMilestoneRequestModel(
    string Name,
    decimal Percentage,
    PaymentDueTrigger DueTrigger,
    DateTimeOffset? DueDate = null,
    int? NetDays = null,
    string? Notes = null);
