namespace Forge.Core.Entities;

public class JobPart : BaseEntity
{
    public int JobId { get; set; }
    public int PartId { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string? Notes { get; set; }

    // Navigation
    public Job Job { get; set; } = null!;
    public Part Part { get; set; } = null!;
}
