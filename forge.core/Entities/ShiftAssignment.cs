namespace Forge.Core.Entities;

public class ShiftAssignment : BaseEntity
{
    public int UserId { get; set; }
    public int ShiftId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal? ShiftDifferentialRate { get; set; }
    public string? Notes { get; set; }

    public Shift Shift { get; set; } = null!;
}
