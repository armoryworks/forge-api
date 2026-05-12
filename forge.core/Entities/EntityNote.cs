namespace Forge.Core.Entities;

public class EntityNote : BaseAuditableEntity
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int? CreatedBy { get; set; }
}
