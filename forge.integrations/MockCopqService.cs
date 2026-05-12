using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockCopqService(ILogger<MockCopqService> logger, IClock clock) : ICopqService
{
    public Task<CopqReportResponseModel> GenerateReportAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        logger.LogInformation("[MockCopq] GenerateReport {Start} to {End}", periodStart, periodEnd);
        return Task.FromResult(new CopqReportResponseModel
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            InternalFailureCost = 12500m,
            ExternalFailureCost = 8200m,
            AppraisalCost = 5400m,
            PreventionCost = 3100m,
            TotalCopq = 29200m,
            Revenue = 450000m,
            CopqAsPercentOfRevenue = 6.49m,
            Details =
            [
                new() { Category = "Internal Failure", SubCategory = "Scrap", Amount = 7500m, EventCount = 12, PercentOfTotal = 25.68m },
                new() { Category = "Internal Failure", SubCategory = "Rework", Amount = 5000m, EventCount = 8, PercentOfTotal = 17.12m },
                new() { Category = "External Failure", SubCategory = "Returns", Amount = 5200m, EventCount = 3, PercentOfTotal = 17.81m },
                new() { Category = "External Failure", SubCategory = "Warranty", Amount = 3000m, EventCount = 2, PercentOfTotal = 10.27m },
                new() { Category = "Appraisal", SubCategory = "Inspection Labor", Amount = 4000m, EventCount = 50, PercentOfTotal = 13.70m },
                new() { Category = "Appraisal", SubCategory = "Testing", Amount = 1400m, EventCount = 15, PercentOfTotal = 4.79m },
                new() { Category = "Prevention", SubCategory = "Training", Amount = 2100m, EventCount = 5, PercentOfTotal = 7.19m },
                new() { Category = "Prevention", SubCategory = "SPC", Amount = 1000m, EventCount = 10, PercentOfTotal = 3.42m },
            ],
            TrendData = [],
            ParetoByDefect = [],
        });
    }

    public Task<IReadOnlyList<CopqTrendPointResponseModel>> GetTrendAsync(int months, CancellationToken ct)
    {
        logger.LogInformation("[MockCopq] GetTrend for {Months} months", months);
        var result = new List<CopqTrendPointResponseModel>();
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        for (var i = months - 1; i >= 0; i--)
        {
            var period = today.AddMonths(-i);
            result.Add(new CopqTrendPointResponseModel
            {
                Period = new DateOnly(period.Year, period.Month, 1),
                InternalFailure = 10000m + Random.Shared.Next(-2000, 3000),
                ExternalFailure = 7000m + Random.Shared.Next(-1500, 2000),
                Appraisal = 5000m + Random.Shared.Next(-500, 1000),
                Prevention = 3000m + Random.Shared.Next(-300, 500),
                Total = 25000m + Random.Shared.Next(-3000, 5000),
            });
        }
        return Task.FromResult<IReadOnlyList<CopqTrendPointResponseModel>>(result);
    }

    public Task<IReadOnlyList<CopqParetoItemResponseModel>> GetParetoByDefectAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        logger.LogInformation("[MockCopq] GetPareto {Start} to {End}", periodStart, periodEnd);
        IReadOnlyList<CopqParetoItemResponseModel> result =
        [
            new() { DefectType = "Dimensional Out-of-Spec", Cost = 8500m, Occurrences = 15, CumulativePercent = 29.1m },
            new() { DefectType = "Surface Finish Defect", Cost = 6200m, Occurrences = 10, CumulativePercent = 50.3m },
            new() { DefectType = "Material Contamination", Cost = 4800m, Occurrences = 5, CumulativePercent = 66.8m },
            new() { DefectType = "Assembly Error", Cost = 3500m, Occurrences = 8, CumulativePercent = 78.8m },
            new() { DefectType = "Cosmetic Damage", Cost = 2200m, Occurrences = 12, CumulativePercent = 86.3m },
            new() { DefectType = "Wrong Part Installed", Cost = 1800m, Occurrences = 4, CumulativePercent = 92.5m },
            new() { DefectType = "Missing Component", Cost = 1200m, Occurrences = 3, CumulativePercent = 96.6m },
            new() { DefectType = "Other", Cost = 1000m, Occurrences = 6, CumulativePercent = 100.0m },
        ];
        return Task.FromResult(result);
    }
}
