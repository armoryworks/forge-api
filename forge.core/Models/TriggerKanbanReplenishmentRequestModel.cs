namespace Forge.Core.Models;

public record TriggerKanbanReplenishmentRequestModel
{
    public string TriggerType { get; init; } = "Manual";
}
