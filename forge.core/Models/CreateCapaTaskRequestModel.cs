namespace Forge.Core.Models;

public record CreateCapaTaskRequestModel
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int AssigneeId { get; init; }
    public DateTimeOffset DueDate { get; init; }
}
