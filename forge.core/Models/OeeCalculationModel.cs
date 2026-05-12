namespace Forge.Core.Models;

public record OeeCalculationModel
{
    public int WorkCenterId { get; init; }
    public string WorkCenterName { get; init; } = string.Empty;
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }

    public decimal ScheduledMinutes { get; init; }
    public decimal PlannedDowntimeMinutes { get; init; }
    public decimal UnplannedDowntimeMinutes { get; init; }
    public decimal RunTimeMinutes { get; init; }

    public decimal TotalQuantity { get; init; }
    public decimal GoodQuantity { get; init; }
    public decimal ScrapQuantity { get; init; }
    public decimal ReworkQuantity { get; init; }

    public decimal Availability { get; init; }
    public decimal Performance { get; init; }
    public decimal Quality { get; init; }

    public decimal Oee => Availability * Performance * Quality;
    public decimal OeePercent => Oee * 100;
    public bool IsWorldClass => Availability >= 0.90m && Performance >= 0.95m && Quality >= 0.995m;
}
