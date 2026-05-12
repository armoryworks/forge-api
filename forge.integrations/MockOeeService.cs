using Microsoft.Extensions.Logging;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockOeeService(ILogger<MockOeeService> logger) : IOeeService
{
    public Task<OeeCalculationModel> CalculateOeeAsync(int workCenterId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        logger.LogInformation("MockOeeService: CalculateOee for WorkCenter {WorkCenterId}, {From} to {To}", workCenterId, from, to);

        var result = new OeeCalculationModel
        {
            WorkCenterId = workCenterId,
            WorkCenterName = $"Work Center {workCenterId}",
            PeriodStart = from,
            PeriodEnd = to,
            ScheduledMinutes = 2400m,
            PlannedDowntimeMinutes = 120m,
            UnplannedDowntimeMinutes = 80m,
            RunTimeMinutes = 2200m,
            TotalQuantity = 1000m,
            GoodQuantity = 985m,
            ScrapQuantity = 10m,
            ReworkQuantity = 5m,
            Availability = 0.965m,
            Performance = 0.92m,
            Quality = 0.985m,
        };

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<OeeCalculationModel>> CalculateOeeForAllWorkCentersAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        logger.LogInformation("MockOeeService: CalculateOeeForAllWorkCenters {From} to {To}", from, to);

        IReadOnlyList<OeeCalculationModel> result = new List<OeeCalculationModel>
        {
            new()
            {
                WorkCenterId = 1,
                WorkCenterName = "CNC Mill",
                PeriodStart = from,
                PeriodEnd = to,
                Availability = 0.92m,
                Performance = 0.88m,
                Quality = 0.995m,
            },
            new()
            {
                WorkCenterId = 2,
                WorkCenterName = "Lathe",
                PeriodStart = from,
                PeriodEnd = to,
                Availability = 0.85m,
                Performance = 0.90m,
                Quality = 0.98m,
            },
        };

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<OeeTrendPointModel>> GetOeeTrendAsync(int workCenterId, DateOnly from, DateOnly to, OeeTrendGranularity granularity, CancellationToken ct)
    {
        logger.LogInformation("MockOeeService: GetOeeTrend for WorkCenter {WorkCenterId}, {Granularity}", workCenterId, granularity);

        var points = new List<OeeTrendPointModel>();
        var current = from;
        while (current <= to)
        {
            points.Add(new OeeTrendPointModel
            {
                Date = current,
                Availability = 0.90m + Random.Shared.Next(0, 10) * 0.01m,
                Performance = 0.85m + Random.Shared.Next(0, 10) * 0.01m,
                Quality = 0.95m + Random.Shared.Next(0, 5) * 0.01m,
                Oee = 0.75m + Random.Shared.Next(0, 15) * 0.01m,
            });

            current = granularity switch
            {
                OeeTrendGranularity.Daily => current.AddDays(1),
                OeeTrendGranularity.Weekly => current.AddDays(7),
                OeeTrendGranularity.Monthly => current.AddMonths(1),
                _ => current.AddDays(1),
            };
        }

        return Task.FromResult<IReadOnlyList<OeeTrendPointModel>>(points);
    }

    public Task<SixBigLossesModel> GetSixBigLossesAsync(int workCenterId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        logger.LogInformation("MockOeeService: GetSixBigLosses for WorkCenter {WorkCenterId}", workCenterId);

        var result = new SixBigLossesModel
        {
            WorkCenterId = workCenterId,
            EquipmentFailureMinutes = 45m,
            SetupAdjustmentMinutes = 60m,
            IdlingMinutes = 30m,
            ReducedSpeedMinutes = 25m,
            ProcessDefectMinutes = 15m,
            ReducedYieldMinutes = 10m,
            TotalLossMinutes = 185m,
        };

        return Task.FromResult(result);
    }
}
