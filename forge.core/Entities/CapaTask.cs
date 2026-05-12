using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class CapaTask : BaseEntity
{
    public int CapaId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int AssigneeId { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public CapaTaskStatus Status { get; set; } = CapaTaskStatus.Open;
    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedById { get; set; }
    public string? CompletionNotes { get; set; }
    public int SortOrder { get; set; }

    // Navigation (no ApplicationUser nav properties — FK-only pattern)
    public CorrectiveAction Capa { get; set; } = null!;
}
