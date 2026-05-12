using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class AndonAlert : BaseEntity
{
    public int WorkCenterId { get; set; }
    public AndonAlertType Type { get; set; }
    public AndonAlertStatus Status { get; set; } = AndonAlertStatus.Active;
    public int RequestedById { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public int? AcknowledgedById { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public int? ResolvedById { get; set; }
    public string? Notes { get; set; }
    public int? JobId { get; set; }

    public WorkCenter WorkCenter { get; set; } = null!;
    public Job? Job { get; set; }
}
