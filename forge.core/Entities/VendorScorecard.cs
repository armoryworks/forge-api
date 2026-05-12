using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class VendorScorecard : BaseEntity
{
    public int VendorId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    // Delivery
    public int TotalPurchaseOrders { get; set; }
    public int TotalLinesReceived { get; set; }
    public int OnTimeDeliveries { get; set; }
    public int LateDeliveries { get; set; }
    public int EarlyDeliveries { get; set; }
    public decimal AvgLeadTimeDays { get; set; }
    public decimal OnTimeDeliveryPercent { get; set; }

    // Quality
    public int TotalInspected { get; set; }
    public int TotalAccepted { get; set; }
    public int TotalRejected { get; set; }
    public int TotalNcrs { get; set; }
    public decimal QualityAcceptancePercent { get; set; }

    // Price
    public decimal TotalSpend { get; set; }
    public decimal AvgPriceVariancePercent { get; set; }
    public int CostIncreaseCount { get; set; }

    // Quantity
    public int QuantityShortages { get; set; }
    public int QuantityOverages { get; set; }
    public decimal QuantityAccuracyPercent { get; set; }

    // Overall
    public decimal OverallScore { get; set; }
    public VendorGrade Grade { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public string? CalculationNotes { get; set; }

    // Navigation
    public Vendor Vendor { get; set; } = null!;
}
