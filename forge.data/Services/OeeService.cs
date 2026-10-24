using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class OeeService(AppDbContext db) : IOeeService
{
    public async Task<OeeCalculationModel> CalculateOeeAsync(
        int workCenterId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (workCenter, capacity) = await LoadWithCapacityAsync(workCenterId, ct);

        var fromOffset = capacity.MidnightUtc(from);
        var toOffset = capacity.MidnightUtc(to.AddDays(1));

        var scheduledMinutes = ScheduledMinutes(capacity, from, to);

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
                     && (r.StartedAt ?? r.CompletedAt) >= fromOffset && (r.StartedAt ?? r.CompletedAt) < toOffset
                     && r.Status != ProductionRunStatus.Planned)
            .ToListAsync(ct);

        // CompletedQuantity is the GOOD count, disjoint from ScrapQuantity (the UpdateProductionRun validator
        // enforces Completed + Scrap ≤ Target) — so total units PROCESSED is good + scrap + rework, not
        // CompletedQuantity alone. (The old code treated CompletedQuantity as the total and double-subtracted
        // scrap/rework, understating throughput and mis-stating the Quality + Performance factors.)
        var goodQuantity = runs.Sum(r => (decimal)r.CompletedQuantity);
        var scrapQuantity = runs.Sum(r => (decimal)r.ScrapQuantity);
        var reworkQuantity = runs.Sum(r => (decimal)r.ReworkQuantity);
        var totalQuantity = goodQuantity + scrapQuantity + reworkQuantity;

        var runTimeMinutes = runs.Sum(r => r.EffectiveRunTimeMinutes() ?? 0m);

        // OEE factors
        var availableTime = scheduledMinutes - plannedDowntimeMinutes;
        var availability = availableTime > 0 ? Math.Min(1m, (availableTime - unplannedDowntimeMinutes) / availableTime) : 0m;
        if (availability < 0) availability = 0;

        var performance = Performance(runs, workCenter.IdealCycleTimeSeconds);

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
            HasProduction = runs.Count > 0,
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
        var (_, capacity) = await LoadWithCapacityAsync(workCenterId, ct);
        var fromOffset = capacity.MidnightUtc(from);
        var toOffset = capacity.MidnightUtc(to.AddDays(1));

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

    private async Task<(WorkCenter WorkCenter, WorkCenterCapacity Capacity)> LoadWithCapacityAsync(int workCenterId, CancellationToken ct)
    {
        var workCenter = await db.WorkCenters
            .AsNoTracking()
            .WithCapacityData()
            .FirstOrDefaultAsync(w => w.Id == workCenterId, ct)
            ?? throw new KeyNotFoundException($"WorkCenter {workCenterId} not found");

        var capacity = (await WorkCenterCapacityLookup.BuildAsync(db, [workCenter], ct))[workCenter.Id];
        return (workCenter, capacity);
    }

    private static decimal ScheduledMinutes(WorkCenterCapacity capacity, DateOnly from, DateOnly to)
    {
        var totalMinutes = 0m;
        for (var date = from; date <= to; date = date.AddDays(1))
            totalMinutes += capacity.HoursOn(date) * 60m;

        return totalMinutes;
    }

    private static decimal Performance(IEnumerable<ProductionRun> runs, decimal? workCenterIdealCycleSeconds)
    {
        var idealMinutes = 0m;
        var timedMinutes = 0m;
        foreach (var run in runs)
        {
            var idealSeconds = run.IdealCycleTimeSeconds ?? workCenterIdealCycleSeconds;
            var runTime = run.EffectiveRunTimeMinutes();
            if (runTime is not > 0 || idealSeconds is not > 0)
                continue;

            var processed = (decimal)run.CompletedQuantity + run.ScrapQuantity + run.ReworkQuantity;
            idealMinutes += processed * idealSeconds.Value / 60m;
            timedMinutes += runTime.Value;
        }

        return timedMinutes > 0 ? Math.Min(1m, idealMinutes / timedMinutes) : 0m;
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
