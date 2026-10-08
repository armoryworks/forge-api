using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class JobOperationEvent : BaseEntity
{
    public int JobOperationId { get; set; }
    public JobOperationEventKind Kind { get; set; }
    public decimal Quantity { get; set; }
    public string? ReasonCode { get; set; }
    public int UserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public JobOperation JobOperation { get; set; } = null!;
}
