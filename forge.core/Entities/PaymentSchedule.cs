using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// Pre-payment schedule header. Defined on a Quote and RE-LINKED (not cloned)
/// to the SalesOrder at conversion — one source of truth across both views.
/// Percentages on the milestones are authoritative; amounts derive from the
/// live document total except where a milestone's amount is locked.
/// </summary>
public class PaymentSchedule : BaseAuditableEntity
{
    public int? QuoteId { get; set; }
    public int? SalesOrderId { get; set; }
    public PaymentScheduleStatus Status { get; set; } = PaymentScheduleStatus.Draft;

    public Quote? Quote { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    public ICollection<PaymentMilestone> Milestones { get; set; } = [];
}
