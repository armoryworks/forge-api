using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class FollowUpTask : BaseAuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int AssignedToUserId { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string SourceEntityType { get; set; } = string.Empty;
    public int SourceEntityId { get; set; }
    public FollowUpTriggerType TriggerType { get; set; }
    public FollowUpStatus Status { get; set; } = FollowUpStatus.Open;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
}
