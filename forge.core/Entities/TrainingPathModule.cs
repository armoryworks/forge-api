namespace Forge.Core.Entities;

public class TrainingPathModule : BaseAuditableEntity
{
    public int PathId { get; set; }
    public int ModuleId { get; set; }
    public int Position { get; set; }
    public bool IsRequired { get; set; } = true;

    public TrainingPath Path { get; set; } = null!;
    public TrainingModule Module { get; set; } = null!;
}
