
namespace Forge.Core.Entities;

public class KanbanTriggerLog : BaseAuditableEntity
{
    public int KanbanCardId { get; set; }
    public KanbanTriggerType TriggerType { get; set; }
    public DateTimeOffset TriggeredAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal? FulfilledQuantity { get; set; }
    public int? OrderId { get; set; }
    public string? OrderType { get; set; }
    public int? TriggeredByUserId { get; set; }

    public KanbanCard KanbanCard { get; set; } = null!;
}
