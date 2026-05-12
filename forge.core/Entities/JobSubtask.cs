namespace Forge.Core.Entities;

public class JobSubtask : BaseAuditableEntity
{
    public int JobId { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public int? AssigneeId { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedById { get; set; }

    public Job Job { get; set; } = null!;
}
