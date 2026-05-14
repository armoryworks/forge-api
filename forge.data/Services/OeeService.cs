using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class OeeService(AppDbContext db) : IOeeService
{
    public async Task<OeeCalculationModel> CalculateOeeAsync(
        int workCenterId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var workCenter = await db.WorkCenters
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workCenterId, ct)
            ?? throw new KeyNotFoundException($"WorkCenter {workCenterId} not found");

        var fromOffset = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Scheduled minutes: calendar overrides or default daily capacity
        var scheduledMinutes = await GetScheduledMinutesAsync(workCenterId, from, to, workCenter.DailyCapacityHours, ct);

        // Downtime
        var downtimeLogs = await db.DowntimeLogs
            .AsNoTracking()
            .Where(d => d.WorkCenterId == workCenterId && d.StartedAt >= fromOffset && d.StartedAt < toOffset)
            .ToListAsync(ct);

        var plannedDowntimeMinutes = downtimeLogs.Where(d => d.IsPlanned).Sum(d => d.DurationMinutes);
        var unplannedDowntimeMinutes = downtimeLogs.Where(d => !d.IsPlanned).Sum(d => d.DurationMinutes);

        // Production runs
        var runs = await db.ProductionRuns
            .AsNoTracking()
            .Where(r => r.WorkCenterId == workCenterId
                     && r.StartedAt >= fromOffset && r.StartedAt < toOffset
                     && r.Status != ProductionRunStatus.Planned)
            .ToListAsync(ct);

        var totalQuantity = runs.Sum(r => (decimal)r.CompletedQuantity);
        var scrapQuantity = runs.Sum(r => (decimal)r.ScrapQuantity);
        var reworkQuantity = runs.Sum(r => (decimal)r.ReworkQuantity);
        var goodQuantity = totalQuantity - scrapQuantity - reworkQuantity;
        if (goodQuantity < 0) goodQuantity = 0;

        var runTimeMinutes = runs.Sum(r => r.RunTimeMinutes ?? 0m);

        // OEE factors
        var availableTime = scheduledMinutes - plannedDowntimeMinutes;
        var availability = availableTime > 0 ? Math.Min(1m, (availableTime - unplannedDowntimeMinutes) / availableTime) : 0m;
        if (availability < 0) availability = 0;

        var idealCycleTimeSeconds = workCenter.IdealCycleTimeSeconds ?? 0m;
        var performance = 0m;
        if (runTimeMinutes > 0 && idealCycleTimeSeconds > 0)
        {
            var idealRunMinutes = totalQuantity * idealCycleTimeSeconds / 60m;
            performance = Math.Min(1m, idealRunMinutes / runTimeMinutes);
        }

        var quality = totalQuantity > 0 ? goodQuantity / totalQuantity : 0m;

        return new OeeCalculationModel
        {
            WorkCenterId = workCenterId,
            WorkCenterName = workCenter.Name,
            PeriodStart = from,
            PeriodEnd = to,
            ScheduledMinutes = scheduledMinutes,
            PlannedDowntimeMinutes = plannedDowntimeMinutes,
            UnplannedDowntimeMinutes = unplannedDowntimeMinutes,
            RunTimeMinutes = runTimeMinutes,
            TotalQuantity = totalQuantity,
            GoodQuantity = goodQuantity,
            ScrapQuantity = scrapQuantity,
            ReworkQuantity = reworkQuantity,
            Availability = Math.Round(availability, 4),
            Performance = Math.Round(performance, 4),
            Quality = Math.Round(quality, 4),
        };
    }

    public async Task<IReadOnlyList<OeeCalculationModel>> CalculateOeeForAllWorkCentersAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var workCenterIds = await db.WorkCenters
            .AsNoTracking()
            .Where(w => w.IsActive)
            .Select(w => w.Id)
            .ToListAsync(ct);

        var results = new List<OeeCalculationModel>(workCenterIds.Count);
        foreach (var id in workCenterIds)
        {
            results.Add(await CalculateOeeAsync(id, from, to, ct));
        }

        return results;
    }

    public async Task<IReadOnlyList<OeeTrendPointModel>> GetOeeTrendAsync(
        int workCenterId, DateOnly from, DateOnly to, OeeTrendGranularity granularity, CancellationToken ct)
    {
        var periods = GeneratePeriods(from, to, granularity);
        var results = new List<OeeTrendPointModel>(periods.Count);

        foreach (var (periodStart, periodEnd) in periods)
        {
            var calc = await CalculateOeeAsync(workCenterId, periodStart, periodEnd, ct);
            results.Add(new OeeTrendPointModel
            {
                Date = periodStart,
                Availability = calc.Availability,
                Performance = calc.Performance,
                Quality = calc.Quality,
                Oee = calc.Oee,
            });
        }

        return results;
    }

    public async Task<SixBigLossesModel> GetSixBigLossesAsync(
        int workCenterId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromOffset = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var downtimeLogs = await db.DowntimeLogs
            .AsNoTracking()
            .Where(d => d.WorkCenterId == workCenterId && d.StartedAt >= fromOffset && d.StartedAt < toOffset)
            .ToListAsync(ct);

        var byCategory = downtimeLogs
            .Where(d => d.Category.HasValue)
            .GroupBy(d => d.Category!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.DurationMinutes));

        decimal Get(DowntimeCategory cat) => byCategory.GetValueOrDefault(cat, 0m);

        var result = new SixBigLossesModel
        {
            WorkCenterId = workCenterId,
            EquipmentFailureMinutes = Get(DowntimeCategory.EquipmentFailure),
            SetupAdjustmentMinutes = Get(DowntimeCategory.SetupAdjustment),
            IdlingMinutes = Get(DowntimeCategory.Idling),
            ReducedSpeedMinutes = Get(DowntimeCategory.ReducedSpeed),
            ProcessDefectMinutes = Get(DowntimeCategory.ProcessDefects),
            ReducedYieldMinutes = Get(DowntimeCategory.ReducedYield),
            TotalLossMinutes = downtimeLogs.Sum(d => d.DurationMinutes),
        };

        return result;
    }

    private async Task<decimal> GetScheduledMinutesAsync(
        int workCenterId, DateOnly from, DateOnly to, decimal defaultDailyHours, CancellationToken ct)
    {
        var calendarOverrides = await db.Set<WorkCenterCalendar>()
            .AsNoTracking()
            .Where(c => c.WorkCenterId == workCenterId && c.Date >= from && c.Date <= to)
            .ToDictionaryAsync(c => c.Date, c => c.AvailableHours, ct);

        var totalMinutes = 0m;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var hours = calendarOverrides.TryGetValue(date, out var overrideHours)
                ? overrideHours
                : defaultDailyHours;
            totalMinutes += hours * 60m;
        }

        return totalMinutes;
    }

    private static List<(DateOnly Start, DateOnly End)> GeneratePeriods(
        DateOnly from, DateOnly to, OeeTrendGranularity granularity)
    {
        var periods = new List<(DateOnly, DateOnly)>();
        var current = from;

        while (current <= to)
        {
            var periodEnd = granularity switch
            {
                OeeTrendGranularity.Daily => current,
                OeeTrendGranularity.Weekly => current.AddDays(6) > to ? to : current.AddDays(6),
                OeeTrendGranularity.Monthly => current.AddMonths(1).AddDays(-1) > to ? to : current.AddMonths(1).AddDays(-1),
                _ => current,
            };

            periods.Add((current, periodEnd));

            current = granularity switch
            {
                OeeTrendGranularity.Daily => current.AddDays(1),
                OeeTrendGranularity.Weekly => current.AddDays(7),
                OeeTrendGranularity.Monthly => current.AddMonths(1),
                _ => current.AddDays(1),
            };
        }

        return periods;
    }
}
