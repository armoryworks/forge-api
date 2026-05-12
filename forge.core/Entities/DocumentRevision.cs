using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class DocumentRevision : BaseEntity
{
    public int DocumentId { get; set; }
    public int RevisionNumber { get; set; }
    public int FileAttachmentId { get; set; }
    public string ChangeDescription { get; set; } = string.Empty;
    public int AuthoredById { get; set; }
    public int? ReviewedById { get; set; }
    public int? ApprovedById { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DocumentRevisionStatus Status { get; set; } = DocumentRevisionStatus.Draft;

    public ControlledDocument Document { get; set; } = null!;
    public FileAttachment FileAttachment { get; set; } = null!;
}
