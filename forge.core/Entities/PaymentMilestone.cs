using System.ComponentModel.DataAnnotations;

using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// One percentage-based fee milestone on a payment schedule. AmountLocked
/// freezes the derived amount once the milestone is invoiced/paid so later
/// document edits can't retroactively change a collected deposit.
/// </summary>
public class PaymentMilestone : BaseAuditableEntity
{
    public int PaymentScheduleId { get; set; }
    public int Sequence { get; set; }
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    public decimal Percentage { get; set; }
    public PaymentDueTrigger DueTrigger { get; set; } = PaymentDueTrigger.OnAcceptance;
    public DateTimeOffset? DueDate { get; set; }
    public int? NetDays { get; set; }
    public PaymentMilestoneStatus Status { get; set; } = PaymentMilestoneStatus.Pending;
    public decimal? AmountLocked { get; set; }
    public int? InvoiceId { get; set; }
    public decimal? PaidAmount { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    [MaxLength(200)]
    public string? PaidReference { get; set; }
    [MaxLength(500)]
    public string? Notes { get; set; }

    public PaymentSchedule PaymentSchedule { get; set; } = null!;
    public Invoice? Invoice { get; set; }
}
