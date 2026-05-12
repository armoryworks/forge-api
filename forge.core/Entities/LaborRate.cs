namespace Forge.Core.Entities;

public class LaborRate : BaseEntity
{
    public int UserId { get; set; }
    public decimal StandardRatePerHour { get; set; }
    public decimal OvertimeRatePerHour { get; set; }
    public decimal? DoubletimeRatePerHour { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Notes { get; set; }
}
