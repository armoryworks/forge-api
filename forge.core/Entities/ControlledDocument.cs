
namespace Forge.Core.Entities;

public class ControlledDocument : BaseAuditableEntity
{
    public string DocumentNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public int CurrentRevision { get; set; } = 1;
    public ControlledDocumentStatus Status { get; set; } = ControlledDocumentStatus.Draft;
    public int OwnerId { get; set; }
    public int? CheckedOutById { get; set; }
    public DateTimeOffset? CheckedOutAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? ReviewDueDate { get; set; }
    public int ReviewIntervalDays { get; set; } = 365;

    public ICollection<DocumentRevision> Revisions { get; set; } = [];
}
