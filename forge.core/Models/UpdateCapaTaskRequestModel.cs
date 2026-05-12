using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateCapaTaskRequestModel
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public int? AssigneeId { get; init; }
    public DateTimeOffset? DueDate { get; init; }
    public CapaTaskStatus? Status { get; init; }
    public string? CompletionNotes { get; init; }
}
