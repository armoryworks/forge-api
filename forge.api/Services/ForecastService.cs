using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class ForecastService(AppDbContext db, IClock clock) : IForecastService
{
    public async Task<DemandForecastResponseModel> GenerateForecastAsync(
        int partId,
        string name,
        ForecastMethod method,
        int historicalPeriods,
        int forecastPeriods,
        double? smoothingFactor,
        int? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var part = await db.Parts.AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => new { p.Id, p.PartNumber, p.Description })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Part {partId} not found.");

        // Gather historical demand from SO lines, grouped by month
        var historyStart = clock.UtcNow.AddMonths(-historicalPeriods);
        var historicalData = await db.SalesOrderLines
            .AsNoTracking()
            .Include(l => l.SalesOrder)
            .Where(l => l.PartId == partId
                && l.SalesOrder!.Status != SalesOrderStatus.Cancelled
                && l.SalesOrder!.CreatedAt >= historyStart)
            .Select(l => new
            {
                Month = new DateTimeOffset(l.SalesOrder!.CreatedAt.Year, l.SalesOrder.CreatedAt.Month, 1, 0, 0, 0, TimeSpan.Zero),
                Quantity = (decimal)l.Quantity,
            })
            .ToListAsync(cancellationToken);

        // Build monthly buckets
        var monthlyHistory = new Dictionary<DateTimeOffset, decimal>();
        for (var i = 0; i < historicalPeriods; i++)
        {
            var monthStart = new DateTimeOffset(
                clock.UtcNow.AddMonths(-historicalPeriods + i).Year,
                clock.UtcNow.AddMonths(-historicalPeriods + i).Month,
                1, 0, 0, 0, TimeSpan.Zero);
            monthlyHistory[monthStart] = 0;
        }

        foreach (var item in historicalData)
        {
            if (monthlyHistory.ContainsKey(item.Month))
                monthlyHistory[item.Month] += item.Quantity;
        }

        var historyValues = monthlyHistory.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();

        // Generate forecast
        var forecastValues = method switch
        {
            ForecastMethod.MovingAverage => ComputeMovingAverage(historyValues, forecastPeriods),
            ForecastMethod.ExponentialSmoothing => ComputeExponentialSmoothing(historyValues, forecastPeriods, smoothingFactor ?? 0.3),
            ForecastMethod.WeightedMovingAverage => ComputeWeightedMovingAverage(historyValues, forecastPeriods),
            _ => ComputeMovingAverage(historyValues, forecastPeriods),
        };

        // Build forecast buckets
        var forecastStart = new DateTimeOffset(clock.UtcNow.Year, clock.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        var buckets = new List<ForecastBucketModel>();

        // Historical buckets
        var historyList = monthlyHistory.OrderBy(kv => kv.Key).ToList();
        foreach (var kv in historyList)
        {
            buckets.Add(new ForecastBucketModel(
                PeriodStart: kv.Key,
                PeriodEnd: kv.Key.AddMonths(1),
                ForecastedQuantity: 0,
                HistoricalQuantity: kv.Value,
                OverrideQuantity: null
            ));
        }

        // Forecast buckets
        for (var i = 0; i < forecastPeriods; i++)
        {
            var periodStart = forecastStart.AddMonths(i);
            buckets.Add(new ForecastBucketModel(
                PeriodStart: periodStart,
                PeriodEnd: periodStart.AddMonths(1),
                ForecastedQuantity: forecastValues[i],
                HistoricalQuantity: null,
                OverrideQuantity: null
            ));
        }

        // Persist
        var forecast = new DemandForecast
        {
            Name = name,
            PartId = partId,
            Method = method,
            Status = ForecastStatus.Draft,
            HistoricalPeriods = historicalPeriods,
            ForecastPeriods = forecastPeriods,
            SmoothingFactor = smoothingFactor,
            ForecastStartDate = forecastStart,
            ForecastDataJson = JsonSerializer.Serialize(buckets),
            CreatedByUserId = createdByUserId,
        };

        db.DemandForecasts.Add(forecast);
        await db.SaveChangesAsync(cancellationToken);

        return new DemandForecastResponseModel(
            forecast.Id,
            forecast.Name,
            partId,
            part.PartNumber,
            part.Description,
            method,
            forecast.Status,
            historicalPeriods,
            forecastPeriods,
            smoothingFactor,
            forecastStart,
            buckets,
            null,
            0,
            forecast.CreatedAt
        );
    }

    private static List<decimal> ComputeMovingAverage(List<decimal> history, int periods)
    {
        if (history.Count == 0)
            return Enumerable.Repeat(0m, periods).ToList();

        var windowSize = Math.Min(history.Count, 3);
        var lastValues = history.TakeLast(windowSize).ToList();
        var average = lastValues.Average();

        return Enumerable.Repeat(Math.Round(average, 2), periods).ToList();
    }

    private static List<decimal> ComputeExponentialSmoothing(List<decimal> history, int periods, double alpha)
    {
        if (history.Count == 0)
            return Enumerable.Repeat(0m, periods).ToList();

        var smoothed = (double)history[0];
        foreach (var value in history.Skip(1))
        {
            smoothed = alpha * (double)value + (1 - alpha) * smoothed;
        }

        var result = new List<decimal>();
        for (var i = 0; i < periods; i++)
        {
            result.Add(Math.Round((decimal)smoothed, 2));
        }

        return result;
    }

    private static List<decimal> ComputeWeightedMovingAverage(List<decimal> history, int periods)
    {
        if (history.Count == 0)
            return Enumerable.Repeat(0m, periods).ToList();

        var windowSize = Math.Min(history.Count, 6);
        var lastValues = history.TakeLast(windowSize).ToList();

        // Weights increase linearly: 1, 2, 3, ...
        var totalWeight = 0m;
        var weightedSum = 0m;
        for (var i = 0; i < lastValues.Count; i++)
        {
            var weight = i + 1;
            weightedSum += lastValues[i] * weight;
            totalWeight += weight;
        }

        var average = Math.Round(weightedSum / totalWeight, 2);
        return Enumerable.Repeat(average, periods).ToList();
    }
}
