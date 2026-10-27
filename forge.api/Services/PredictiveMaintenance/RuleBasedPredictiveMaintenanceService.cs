using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Assets;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services.PredictiveMaintenance;

/// <summary>
/// Built-in predictive maintenance for installs with no machine-learning integration. A prediction run for a work
/// center checks every asset there (its machines, the asset the work center is, and the tooling used on its runs in
/// the last 120 days) against four rules:
/// <list type="bullet">
/// <item><b>usage</b>: shots or hours since the last service reach the usage threshold (90 %) of a PM interval; the due
/// date is projected from the last 30 days of use.</item>
/// <item><b>downtime-trend</b>: two or more unplanned stops in the last 30 days, and the count or minutes at least the
/// trend factor (2×) of the 30-day average over the 90 days before.</item>
/// <item><b>cycle-drift</b>: over the last N timed runs (5) actual time is more than the drift ratio (1.15×) of ideal.</item>
/// <item><b>scrap-trend</b>: the scrap rate on a tool's runs in the last 30 days is at least the trend factor (2×) of
/// its rate over the 90 days before, or 5 % when it had none.</item>
/// </list>
/// Confidence is a fixed tier per rule. A rule raises at most one open prediction per asset; an open prediction
/// whose condition has cleared expires; a prediction marked a false positive (or with feedback that no failure
/// happened) silences that rule for that asset for the suppression window (30 days).
/// </summary>
public class RuleBasedPredictiveMaintenanceService(AppDbContext db, IClock clock) : IPredictiveMaintenanceService
{
    public const string RuleModelId = "forge-rules";
    public const string RuleModelVersion = "1";
    public const string UsageRule = "usage";
    public const string DowntimeTrendRule = "downtime-trend";
    public const string CycleDriftRule = "cycle-drift";
    public const string ScrapTrendRule = "scrap-trend";

    private const int RecentDays = 30;
    private const int PriorDays = 90;
    private const decimal NoHistoryScrapRate = 0.05m;

    private static readonly MaintenancePredictionStatus[] OpenStatuses =
    [
        MaintenancePredictionStatus.Predicted,
        MaintenancePredictionStatus.Acknowledged,
        MaintenancePredictionStatus.MaintenanceScheduled,
    ];

    public async Task TriggerPredictionRunAsync(int workCenterId, CancellationToken ct) =>
        await RunForWorkCenterAsync(workCenterId, ct);

    /// <summary>Evaluates the work center's assets and stores new predictions. Returns how many were raised.</summary>
    public async Task<int> RunForWorkCenterAsync(int workCenterId, CancellationToken ct)
    {
        var workCenter = await db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workCenterId, ct)
            ?? throw new KeyNotFoundException($"Work center {workCenterId} not found.");

        var now = clock.UtcNow;
        var windowStart = now.AddDays(-(RecentDays + PriorDays));

        var toolIds = await db.ProductionRuns.AsNoTracking()
            .Where(r => r.WorkCenterId == workCenterId && r.ToolingAssetId != null
                && (r.CompletedAt ?? r.StartedAt ?? r.CreatedAt) >= windowStart)
            .Select(r => r.ToolingAssetId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var assets = await db.Assets.AsNoTracking()
            .Where(a => a.Status != AssetStatus.Retired
                && (a.WorkCenterId == workCenterId || a.Id == workCenter.AssetId || toolIds.Contains(a.Id)))
            .ToListAsync(ct);
        if (assets.Count == 0)
            return 0;

        var thresholdsFor = await PredictiveRuleThresholds.LoadAsync(db, ct);
        var candidates = new List<PredictionCandidate>();
        foreach (var asset in assets)
            candidates.AddRange(await EvaluateAssetAsync(asset, workCenter, thresholdsFor(asset.AssetType), now, ct));

        return await StoreAsync(workCenterId, assets, candidates, thresholdsFor, now, ct);
    }

    private async Task<List<PredictionCandidate>> EvaluateAssetAsync(
        Asset asset, WorkCenter workCenter, PredictiveRuleThresholds thresholds, DateTimeOffset now, CancellationToken ct)
    {
        var found = new List<PredictionCandidate>();
        var recentStart = now.AddDays(-RecentDays);
        var priorStart = recentStart.AddDays(-PriorDays);
        var isTool = asset.AssetType == AssetType.Tooling;
        var machineWorkCenterId = isTool ? (int?)null : asset.WorkCenterId ?? (workCenter.AssetId == asset.Id ? workCenter.Id : (int?)null);

        var runs = await db.ProductionRuns.AsNoTracking()
            .Where(r => r.Status == ProductionRunStatus.Completed
                && (r.CompletedAt ?? r.StartedAt ?? r.CreatedAt) >= priorStart
                && (isTool ? r.ToolingAssetId == asset.Id : machineWorkCenterId != null && r.WorkCenterId == machineWorkCenterId))
            .ToListAsync(ct);

        if (await UsageAsync(asset, runs, thresholds, now, ct) is { } usage)
            found.Add(usage);
        if (await DowntimeTrendAsync(asset, thresholds, now, ct) is { } downtime)
            found.Add(downtime);
        if (await CycleDriftAsync(asset, runs, thresholds, now, ct) is { } drift)
            found.Add(drift);
        if (isTool && ScrapTrend(asset, runs, thresholds, now) is { } scrap)
            found.Add(scrap);

        return found;
    }

    private async Task<PredictionCandidate?> UsageAsync(
        Asset asset, List<ProductionRun> runs, PredictiveRuleThresholds thresholds, DateTimeOffset now, CancellationToken ct)
    {
        var schedules = await db.MaintenanceSchedules.AsNoTracking()
            .Where(s => s.AssetId == asset.Id && s.IsActive && (s.IntervalShots > 0 || s.IntervalHours > 0))
            .ToListAsync(ct);
        if (schedules.Count == 0)
            return null;

        var lastService = await MaintenanceUsage.LastServiceReadingsAsync(db, schedules.Select(s => s.Id).ToList(), ct);
        var recentStart = now.AddDays(-RecentDays);
        var recentRuns = runs.Where(r => RunTime(r) >= recentStart).ToList();
        var shotsPerDay = recentRuns.Sum(r => (decimal)r.ShotsApplied) / RecentDays;
        var hoursPerDay = recentRuns.Sum(r => r.EffectiveRunTimeMinutes() ?? 0m) / 60m / RecentDays;

        PredictionCandidate? worst = null;
        var worstRatio = 0m;
        foreach (var schedule in schedules)
        {
            var reading = lastService.GetValueOrDefault(schedule.Id);
            var measures = new List<(string Unit, decimal Used, decimal Interval, decimal PerDay)>();
            if (schedule.IntervalShots is > 0)
                measures.Add(("shots", asset.CurrentShotCount - (reading?.Shots ?? 0), schedule.IntervalShots.Value, shotsPerDay));
            if (schedule.IntervalHours is > 0)
                measures.Add(("hours", asset.CurrentHours - (reading?.Hours ?? 0m), schedule.IntervalHours.Value, hoursPerDay));

            foreach (var (unit, used, interval, perDay) in measures)
            {
                var ratio = used / interval;
                if (ratio * 100m < thresholds.UsageThresholdPercent || ratio <= worstRatio)
                    continue;

                var remaining = Math.Max(0m, interval - used);
                decimal? daysLeft = remaining == 0m ? 0m : perDay > 0m ? remaining / perDay : null;
                var due = daysLeft is decimal d ? now.AddDays((double)Math.Min(d, 365m)) : now.AddDays(RecentDays);
                worstRatio = ratio;
                worst = new PredictionCandidate(
                    asset.Id,
                    UsageRule,
                    "ServiceDueSoon",
                    ratio >= 1m ? 90m : 80m,
                    ratio >= 1m ? MaintenancePredictionSeverity.High : MaintenancePredictionSeverity.Medium,
                    due,
                    daysLeft is decimal left ? Math.Round(left * 24m, 2) : null,
                    new Dictionary<string, object?>
                    {
                        ["schedule"] = schedule.Title,
                        ["unit"] = unit,
                        ["usedSinceService"] = used,
                        ["interval"] = interval,
                        ["percentOfInterval"] = Math.Round(ratio * 100m, 1),
                        ["usagePerDay30d"] = Math.Round(perDay, 2),
                        ["thresholdPercent"] = thresholds.UsageThresholdPercent,
                    });
            }
        }

        return worst;
    }

    private async Task<PredictionCandidate?> DowntimeTrendAsync(
        Asset asset, PredictiveRuleThresholds thresholds, DateTimeOffset now, CancellationToken ct)
    {
        var recentStart = now.AddDays(-RecentDays);
        var priorStart = recentStart.AddDays(-PriorDays);
        var logs = await db.DowntimeLogs.AsNoTracking()
            .Where(d => d.AssetId == asset.Id && !d.IsPlanned && d.StartedAt >= priorStart && d.StartedAt <= now)
            .Select(d => new { d.StartedAt, d.EndedAt })
            .ToListAsync(ct);

        decimal Minutes(DateTimeOffset start, DateTimeOffset? end) => (decimal)((end ?? now) - start).TotalMinutes;
        var recent = logs.Where(l => l.StartedAt >= recentStart).ToList();
        var prior = logs.Where(l => l.StartedAt < recentStart).ToList();
        var recentCount = recent.Count;
        var recentMinutes = recent.Sum(l => Minutes(l.StartedAt, l.EndedAt));
        var priorAvgCount = prior.Count / (PriorDays / (decimal)RecentDays);
        var priorAvgMinutes = prior.Sum(l => Minutes(l.StartedAt, l.EndedAt)) / (PriorDays / (decimal)RecentDays);

        if (recentCount < 2)
            return null;
        var countRising = recentCount >= thresholds.DowntimeTrendFactor * priorAvgCount;
        var minutesRising = recentMinutes > 0m && recentMinutes >= thresholds.DowntimeTrendFactor * priorAvgMinutes;
        if (!countRising && !minutesRising)
            return null;

        var multiple = priorAvgCount > 0m ? recentCount / priorAvgCount : (decimal?)null;
        return new PredictionCandidate(
            asset.Id,
            DowntimeTrendRule,
            "RisingBreakdownRisk",
            70m,
            multiple is null or >= 3m ? MaintenancePredictionSeverity.High : MaintenancePredictionSeverity.Medium,
            now.AddDays(7),
            null,
            new Dictionary<string, object?>
            {
                ["unplannedStops30d"] = recentCount,
                ["unplannedMinutes30d"] = Math.Round(recentMinutes, 1),
                ["priorAverageStopsPer30d"] = Math.Round(priorAvgCount, 2),
                ["priorAverageMinutesPer30d"] = Math.Round(priorAvgMinutes, 1),
                ["trendFactor"] = thresholds.DowntimeTrendFactor,
            });
    }

    private async Task<PredictionCandidate?> CycleDriftAsync(
        Asset asset, List<ProductionRun> runs, PredictiveRuleThresholds thresholds, DateTimeOffset now, CancellationToken ct)
    {
        var workCenterIds = runs.Where(r => r.WorkCenterId != null).Select(r => r.WorkCenterId!.Value).Distinct().ToList();
        var idealByWorkCenter = await db.WorkCenters.AsNoTracking()
            .Where(w => workCenterIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.IdealCycleTimeSeconds, ct);

        var timed = runs
            .Select(r => new
            {
                Run = r,
                Ideal = r.IdealCycleTimeSeconds ?? (r.WorkCenterId is int wc ? idealByWorkCenter.GetValueOrDefault(wc) : null),
                Minutes = r.EffectiveRunTimeMinutes(),
                Pieces = (decimal)r.CompletedQuantity + r.ScrapQuantity + r.ReworkQuantity,
            })
            .Where(x => x.Ideal is > 0 && x.Minutes is > 0 && x.Pieces > 0)
            .OrderByDescending(x => RunTime(x.Run))
            .ThenByDescending(x => x.Run.Id)
            .Take(thresholds.CycleDriftRuns)
            .ToList();
        if (timed.Count < thresholds.CycleDriftRuns)
            return null;

        var actualSeconds = timed.Sum(x => x.Minutes!.Value * 60m);
        var idealSeconds = timed.Sum(x => x.Pieces * x.Ideal!.Value);
        var ratio = actualSeconds / idealSeconds;
        if (ratio <= thresholds.CycleDriftRatio)
            return null;

        return new PredictionCandidate(
            asset.Id,
            CycleDriftRule,
            "PerformanceDegradation",
            65m,
            ratio >= 1.3m ? MaintenancePredictionSeverity.High : MaintenancePredictionSeverity.Medium,
            now.AddDays(14),
            null,
            new Dictionary<string, object?>
            {
                ["runsChecked"] = timed.Count,
                ["actualToIdealRatio"] = Math.Round(ratio, 3),
                ["driftRatio"] = thresholds.CycleDriftRatio,
                ["runNumbers"] = timed.Select(x => x.Run.RunNumber).ToList(),
            });
    }

    private static PredictionCandidate? ScrapTrend(
        Asset asset, List<ProductionRun> runs, PredictiveRuleThresholds thresholds, DateTimeOffset now)
    {
        var recentStart = now.AddDays(-RecentDays);
        var recent = runs.Where(r => RunTime(r) >= recentStart).ToList();
        var prior = runs.Where(r => RunTime(r) < recentStart).ToList();
        if (recent.Count < 2)
            return null;

        static decimal Rate(List<ProductionRun> set)
        {
            var pieces = set.Sum(r => (decimal)r.CompletedQuantity + r.ScrapQuantity);
            return pieces > 0m ? set.Sum(r => (decimal)r.ScrapQuantity) / pieces : 0m;
        }

        var recentRate = Rate(recent);
        var priorRate = Rate(prior);
        var rising = priorRate > 0m ? recentRate >= thresholds.ScrapTrendFactor * priorRate : recentRate >= NoHistoryScrapRate;
        if (!rising || recentRate <= 0m)
            return null;

        return new PredictionCandidate(
            asset.Id,
            ScrapTrendRule,
            "RisingScrap",
            60m,
            recentRate >= 0.1m ? MaintenancePredictionSeverity.High : MaintenancePredictionSeverity.Medium,
            now.AddDays(14),
            null,
            new Dictionary<string, object?>
            {
                ["runs30d"] = recent.Count,
                ["scrapRate30dPercent"] = Math.Round(recentRate * 100m, 2),
                ["scrapRatePrior90dPercent"] = Math.Round(priorRate * 100m, 2),
                ["trendFactor"] = thresholds.ScrapTrendFactor,
            });
    }

    private async Task<int> StoreAsync(
        int workCenterId,
        List<Asset> assets,
        List<PredictionCandidate> candidates,
        Func<AssetType, PredictiveRuleThresholds> thresholdsFor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var assetIds = assets.Select(a => a.Id).ToList();
        var assetTypes = assets.ToDictionary(a => a.Id, a => a.AssetType);
        var history = await db.MaintenancePredictions
            .Where(p => p.AssetId != null && assetIds.Contains(p.AssetId.Value) && p.RuleKey != null)
            .ToListAsync(ct);
        var historyIds = history.Select(p => p.Id).ToList();
        var noFailureFeedback = await db.PredictionFeedbacks.AsNoTracking()
            .Where(f => historyIds.Contains(f.PredictionId) && !f.ActualFailureOccurred)
            .Select(f => f.PredictionId)
            .ToListAsync(ct);
        var noFailure = noFailureFeedback.ToHashSet();

        var open = history.Where(p => OpenStatuses.Contains(p.Status)).ToList();
        var openKeys = open.Select(p => (p.AssetId!.Value, p.RuleKey!)).ToHashSet();
        var suppressed = history
            .Where(p => p.ResolvedAt is DateTimeOffset at
                && at >= now.AddDays(-thresholdsFor(assetTypes[p.AssetId!.Value]).SuppressionDays)
                && (p.Status == MaintenancePredictionStatus.FalsePositive || noFailure.Contains(p.Id)))
            .Select(p => (p.AssetId!.Value, p.RuleKey!))
            .ToHashSet();

        var created = 0;
        foreach (var candidate in candidates)
        {
            var key = (candidate.AssetId, candidate.RuleKey);
            if (openKeys.Contains(key) || suppressed.Contains(key))
                continue;

            db.MaintenancePredictions.Add(new MaintenancePrediction
            {
                WorkCenterId = workCenterId,
                AssetId = candidate.AssetId,
                RuleKey = candidate.RuleKey,
                PredictionType = candidate.PredictionType,
                ConfidencePercent = candidate.ConfidencePercent,
                Severity = candidate.Severity,
                PredictedFailureDate = candidate.PredictedFailureDate,
                RemainingUsefulLifeHours = candidate.RemainingUsefulLifeHours,
                ModelId = RuleModelId,
                ModelVersion = RuleModelVersion,
                InputFeaturesJson = JsonSerializer.Serialize(candidate.Evidence),
                Status = MaintenancePredictionStatus.Predicted,
                PredictedAt = now,
            });
            openKeys.Add(key);
            created++;
        }

        var firing = candidates.Select(c => (c.AssetId, c.RuleKey)).ToHashSet();
        foreach (var stale in open.Where(p => p.ModelId == RuleModelId
            && p.Status is MaintenancePredictionStatus.Predicted or MaintenancePredictionStatus.Acknowledged
            && !firing.Contains((p.AssetId!.Value, p.RuleKey!))))
        {
            stale.Status = MaintenancePredictionStatus.Expired;
            stale.ResolvedAt = now;
            stale.ResolutionNotes = "The condition that raised this prediction has cleared.";
        }

        await db.SaveChangesAsync(ct);
        return created;
    }

    private static DateTimeOffset RunTime(ProductionRun run) => run.CompletedAt ?? run.StartedAt ?? run.CreatedAt;

    public async Task<IReadOnlyList<MaintenancePrediction>> GetActivePredictionsAsync(int? workCenterId, CancellationToken ct) =>
        await db.MaintenancePredictions.AsNoTracking()
            .Where(p => OpenStatuses.Contains(p.Status) && (workCenterId == null || p.WorkCenterId == workCenterId))
            .OrderBy(p => p.PredictedFailureDate)
            .ToListAsync(ct);

    public async Task<MaintenancePrediction> GetPredictionAsync(int predictionId, CancellationToken ct) =>
        await db.MaintenancePredictions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == predictionId, ct)
            ?? throw new KeyNotFoundException($"Prediction {predictionId} not found");

    public async Task AcknowledgePredictionAsync(int predictionId, int userId, CancellationToken ct)
    {
        var prediction = await db.MaintenancePredictions.FirstOrDefaultAsync(p => p.Id == predictionId, ct)
            ?? throw new KeyNotFoundException($"Prediction {predictionId} not found");
        if (prediction.Status != MaintenancePredictionStatus.Predicted)
            return;
        prediction.Status = MaintenancePredictionStatus.Acknowledged;
        prediction.AcknowledgedAt = clock.UtcNow;
        prediction.AcknowledgedByUserId = userId;
        await db.SaveChangesAsync(ct);
    }

    public Task ScheduleMaintenanceAsync(int predictionId, CancellationToken ct) => Task.CompletedTask;

    public Task ResolvePredictionAsync(int predictionId, string notes, CancellationToken ct) =>
        CloseAsync(predictionId, MaintenancePredictionStatus.Resolved, notes, ct);

    public Task MarkFalsePositiveAsync(int predictionId, string notes, CancellationToken ct) =>
        CloseAsync(predictionId, MaintenancePredictionStatus.FalsePositive, notes, ct);

    private async Task CloseAsync(int predictionId, MaintenancePredictionStatus status, string notes, CancellationToken ct)
    {
        var prediction = await db.MaintenancePredictions.FirstOrDefaultAsync(p => p.Id == predictionId, ct)
            ?? throw new KeyNotFoundException($"Prediction {predictionId} not found");
        prediction.Status = status;
        prediction.ResolutionNotes = notes;
        prediction.ResolvedAt = clock.UtcNow;
        if (status == MaintenancePredictionStatus.FalsePositive)
            prediction.WasAccurate = false;
        await db.SaveChangesAsync(ct);
    }

    public Task RecordFeedbackAsync(int predictionId, RecordPredictionFeedbackRequestModel feedback, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<MlModel>> GetModelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MlModel>>([]);

    public async Task<MlModelPerformanceResponseModel> GetModelPerformanceAsync(string modelId, CancellationToken ct)
    {
        var judged = await db.MaintenancePredictions.AsNoTracking()
            .Where(p => p.ModelId == modelId
                && (p.Status == MaintenancePredictionStatus.FalsePositive || db.PredictionFeedbacks.Any(f => f.PredictionId == p.Id)))
            .Select(p => p.WasAccurate)
            .ToListAsync(ct);
        var total = await db.MaintenancePredictions.AsNoTracking().CountAsync(p => p.ModelId == modelId, ct);
        var correct = judged.Count(accurate => accurate);
        var accuracy = judged.Count == 0 ? 0m : Math.Round((decimal)correct / judged.Count, 4);

        return new MlModelPerformanceResponseModel
        {
            ModelId = modelId,
            Accuracy = accuracy,
            Precision = accuracy,
            Recall = 0m,
            F1Score = 0m,
            TotalPredictions = total,
            TruePredictions = correct,
            FalsePredictions = judged.Count - correct,
            AverageLeadTimeHours = 0m,
            AccuracyTrend = [],
        };
    }

    public async Task<PredictiveMaintenanceDashboardResponseModel> GetDashboardAsync(CancellationToken ct)
    {
        var active = await GetActivePredictionsAsync(null, ct);
        return new PredictiveMaintenanceDashboardResponseModel
        {
            ActivePredictions = active.Count,
            CriticalPredictions = active.Count(p => p.Severity == MaintenancePredictionSeverity.Critical),
            PendingAcknowledgment = active.Count(p => p.Status == MaintenancePredictionStatus.Predicted),
            MaintenanceScheduled = active.Count(p => p.Status == MaintenancePredictionStatus.MaintenanceScheduled),
            OverallModelAccuracy = 0m,
            EstimatedDowntimePreventedHours = 0m,
            WorkCenterRisks = [],
            UpcomingPredictions = [],
        };
    }
}
