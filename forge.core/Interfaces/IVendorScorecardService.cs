using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface IVendorScorecardService
{
    Task<VendorScorecard> CalculateScorecardAsync(int vendorId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);
    Task RecalculateAllAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);
    decimal CalculateOverallScore(decimal onTimePercent, decimal qualityPercent, decimal priceScore, decimal quantityAccuracyPercent);
    VendorGrade DetermineGrade(decimal overallScore);
}
