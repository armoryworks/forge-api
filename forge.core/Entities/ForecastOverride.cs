namespace Forge.Core.Entities;

public class ForecastOverride : BaseEntity
{
    public int DemandForecastId { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public decimal OriginalQuantity { get; set; }
    public decimal OverrideQuantity { get; set; }
    public string? Reason { get; set; }
    public int? OverriddenByUserId { get; set; }

    public DemandForecast DemandForecast { get; set; } = null!;
}
