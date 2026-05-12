namespace Forge.Core.Models;

public record KanbanCardDetailResponseModel
{
    public KanbanCardResponseModel Card { get; init; } = null!;
    public IReadOnlyList<KanbanTriggerLogResponseModel> TriggerLogs { get; init; } = [];
}
