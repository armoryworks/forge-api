using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class WbsCostEntry : BaseAuditableEntity
{
    public int WbsElementId { get; set; }
    public WbsCostCategory Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? SourceEntityType { get; set; }
    public int? SourceEntityId { get; set; }
    public DateTimeOffset EntryDate { get; set; }

    public WbsElement WbsElement { get; set; } = null!;
}
