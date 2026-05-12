namespace Forge.Core.Models;

public record KanbanBoardWorkCenterResponseModel
{
    public int WorkCenterId { get; init; }
    public string WorkCenterName { get; init; } = string.Empty;
    public IReadOnlyList<KanbanCardResponseModel> Cards { get; init; } = [];
}
