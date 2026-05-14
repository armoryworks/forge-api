
namespace Forge.Core.Models;

public record CapaTaskResponseModel
{
    public int Id { get; init; }
    public int CapaId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int AssigneeId { get; init; }
    public string AssigneeName { get; init; } = string.Empty;
    public DateTimeOffset DueDate { get; init; }
    public CapaTaskStatus Status { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public int? CompletedById { get; init; }
    public string? CompletedByName { get; init; }
    public string? CompletionNotes { get; init; }
    public int SortOrder { get; init; }
}
