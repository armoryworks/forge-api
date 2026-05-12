namespace Forge.Core.Models;

public record KanbanTriggerLogResponseModel
{
    public int Id { get; init; }
    public string TriggerType { get; init; } = string.Empty;
    public DateTimeOffset TriggeredAt { get; init; }
    public DateTimeOffset? FulfilledAt { get; init; }
    public decimal RequestedQuantity { get; init; }
    public decimal? FulfilledQuantity { get; init; }
    public int? OrderId { get; init; }
    public string? OrderType { get; init; }
    public string? TriggeredByName { get; init; }
}
