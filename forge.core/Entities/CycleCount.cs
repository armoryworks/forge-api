namespace Forge.Core.Entities;

public class CycleCount : BaseAuditableEntity
{
    public int LocationId { get; set; }
    public int CountedById { get; set; }
    public DateTimeOffset CountedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Notes { get; set; }

    public StorageLocation Location { get; set; } = null!;
    // CountedBy navigation configured in CycleCountConfiguration (Data project)
    public ICollection<CycleCountLine> Lines { get; set; } = [];
}
