using Forge.Core.Enums;

namespace Forge.Core.Models;

public record VendorScorecardResponseModel(
    int Id,
    int VendorId,
    string VendorName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int TotalPurchaseOrders,
    int TotalLinesReceived,
    int OnTimeDeliveries,
    int LateDeliveries,
    int EarlyDeliveries,
    decimal AvgLeadTimeDays,
    decimal OnTimeDeliveryPercent,
    int TotalInspected,
    int TotalAccepted,
    int TotalRejected,
    int TotalNcrs,
    decimal QualityAcceptancePercent,
    decimal TotalSpend,
    decimal AvgPriceVariancePercent,
    int CostIncreaseCount,
    int QuantityShortages,
    int QuantityOverages,
    decimal QuantityAccuracyPercent,
    decimal OverallScore,
    VendorGrade Grade,
    DateTimeOffset CalculatedAt,
    string? CalculationNotes);
