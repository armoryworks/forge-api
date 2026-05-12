namespace Forge.Core.Entities;

public class Team : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
