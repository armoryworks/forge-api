namespace Forge.Core.Entities;

public class LaborRate : BaseEntity
{
    public int UserId { get; set; }
    public decimal StandardRatePerHour { get; set; }
    // The actual burdened pay rate (standard costing): time is costed to WIP at standard, and the
    // (actual − standard) × hours difference is the labor RATE variance. Null ⇒ no rate variance (actual = std).
    public decimal? ActualRatePerHour { get; set; }
    public decimal OvertimeRatePerHour { get; set; }
    public decimal? DoubletimeRatePerHour { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Notes { get; set; }
}
