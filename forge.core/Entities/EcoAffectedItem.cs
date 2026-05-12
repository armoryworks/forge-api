namespace Forge.Core.Entities;

public class EcoAffectedItem : BaseEntity
{
    public int EcoId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string ChangeDescription { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public bool IsImplemented { get; set; }

    public EngineeringChangeOrder Eco { get; set; } = null!;
}
