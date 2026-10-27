using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Maintenance;
using Forge.Api.Services.PredictiveMaintenance;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class RuleBasedPredictiveMaintenanceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly RuleBasedPredictiveMaintenanceService _service;

    public RuleBasedPredictiveMaintenanceServiceTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _service = new RuleBasedPredictiveMaintenanceService(_db, _clock.Object);
    }

    [Fact]
    public async Task Usage_rule_fires_at_ninety_percent_of_the_shot_interval_and_projects_the_due_date()
    {
        var (press, mold) = await SeedAsync(moldShots: 4600);
        AddSchedule(mold, intervalShots: 5000);
        AddRun(press, mold, daysAgo: 5, good: 4000, shots: 1000);
        await _db.SaveChangesAsync();

        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        var prediction = await SingleAsync(RuleBasedPredictiveMaintenanceService.UsageRule);
        prediction.AssetId.Should().Be(mold.Id);
        prediction.WorkCenterId.Should().Be(press.Id);
        prediction.PredictionType.Should().Be("ServiceDueSoon");
        prediction.ConfidencePercent.Should().Be(80m);
        prediction.ModelId.Should().Be(RuleBasedPredictiveMaintenanceService.RuleModelId);
        prediction.PredictedFailureDate.Should().BeCloseTo(Now.AddDays(12), TimeSpan.FromMinutes(1), "400 shots left at 1000 shots / 30 days");
        prediction.InputFeaturesJson.Should().Contain("percentOfInterval");
    }

    [Fact]
    public async Task Usage_rule_does_not_fire_below_the_threshold()
    {
        var (press, mold) = await SeedAsync(moldShots: 4000);
        AddSchedule(mold, intervalShots: 5000);
        await _db.SaveChangesAsync();

        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        (await _db.MaintenancePredictions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Usage_threshold_can_be_lowered_for_one_asset_class()
    {
        var (press, mold) = await SeedAsync(moldShots: 4000);
        AddSchedule(mold, intervalShots: 5000);
        _db.SystemSettings.Add(new SystemSetting
        {
            Key = PredictiveMaintenanceSettings.AssetClassOverridesKey,
            Value = "{\"Tooling\": {\"usage-threshold-percent\": 75}}",
        });
        await _db.SaveChangesAsync();

        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        (await SingleAsync(RuleBasedPredictiveMaintenanceService.UsageRule)).AssetId.Should().Be(mold.Id);
    }

    [Fact]
    public async Task Rules_dedupe_while_a_prediction_is_open()
    {
        var (press, mold) = await SeedAsync(moldShots: 4900);
        AddSchedule(mold, intervalShots: 5000);
        await _db.SaveChangesAsync();

        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);
        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        (await _db.MaintenancePredictions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_false_positive_silences_the_rule_for_that_asset_for_thirty_days()
    {
        var (press, mold) = await SeedAsync(moldShots: 4900);
        AddSchedule(mold, intervalShots: 5000);
        await _db.SaveChangesAsync();
        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);
        var first = await SingleAsync(RuleBasedPredictiveMaintenanceService.UsageRule);

        await new MarkFalsePositiveHandler(_db, _clock.Object)
            .Handle(new MarkFalsePositiveCommand(first.Id, new ResolvePredictionRequestModel { Notes = "Just serviced" }), CancellationToken.None);
        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);
        (await _db.MaintenancePredictions.CountAsync()).Should().Be(1);

        _clock.Setup(c => c.UtcNow).Returns(Now.AddDays(31));
        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);
        (await _db.MaintenancePredictions.CountAsync(p => p.Status == MaintenancePredictionStatus.Predicted)).Should().Be(1);
    }

    [Fact]
    public async Task Open_prediction_expires_when_its_condition_clears()
    {
        var (press, mold) = await SeedAsync(moldShots: 4900);
        var schedule = AddSchedule(mold, intervalShots: 5000);
        await _db.SaveChangesAsync();
        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        _db.MaintenanceLogs.Add(new MaintenanceLog { MaintenanceScheduleId = schedule.Id, PerformedAt = Now, ShotCountAtService = 4900 });
        await _db.SaveChangesAsync();
        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        (await SingleAsync(RuleBasedPredictiveMaintenanceService.UsageRule)).Status.Should().Be(MaintenancePredictionStatus.Expired);
    }

    [Fact]
    public async Task Downtime_trend_fires_when_stops_double_against_the_prior_average()
    {
        var (press, _) = await SeedAsync(moldShots: 0);
        var machine = new Asset { Name = "Press 1", AssetType = AssetType.Machine, WorkCenterId = press.Id };
        _db.Assets.Add(machine);
        await _db.SaveChangesAsync();
        foreach (var daysAgo in new[] { 2, 6, 11, 20 })
            AddStop(machine, daysAgo);
        foreach (var daysAgo in new[] { 45, 100 })
            AddStop(machine, daysAgo);
        AddStop(machine, 3, planned: true);
        await _db.SaveChangesAsync();

        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        var prediction = await SingleAsync(RuleBasedPredictiveMaintenanceService.DowntimeTrendRule);
        prediction.AssetId.Should().Be(machine.Id);
        prediction.PredictionType.Should().Be("RisingBreakdownRisk");
        prediction.InputFeaturesJson.Should().Contain("\"unplannedStops30d\":4");
    }

    [Fact]
    public async Task Downtime_trend_does_not_fire_when_stops_hold_steady()
    {
        var (press, _) = await SeedAsync(moldShots: 0);
        var machine = new Asset { Name = "Press 1", AssetType = AssetType.Machine, WorkCenterId = press.Id };
        _db.Assets.Add(machine);
        await _db.SaveChangesAsync();
        foreach (var daysAgo in new[] { 5, 15, 35, 45, 65, 75, 95, 105 })
            AddStop(machine, daysAgo);
        await _db.SaveChangesAsync();

        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        (await _db.MaintenancePredictions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Cycle_drift_fires_when_recent_runs_take_more_than_fifteen_percent_over_ideal()
    {
        var (press, mold) = await SeedAsync(moldShots: 0);
        for (var i = 1; i <= 5; i++)
            AddRun(press, mold, daysAgo: i, good: 100, shots: 25, runMinutes: 70m, idealSeconds: 30m);
        await _db.SaveChangesAsync();

        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        var prediction = await SingleAsync(RuleBasedPredictiveMaintenanceService.CycleDriftRule);
        prediction.PredictionType.Should().Be("PerformanceDegradation");
        prediction.Severity.Should().Be(MaintenancePredictionSeverity.High);
    }

    [Fact]
    public async Task Cycle_drift_does_not_fire_on_runs_at_ideal_speed_or_with_too_few_runs()
    {
        var (press, mold) = await SeedAsync(moldShots: 0);
        for (var i = 1; i <= 5; i++)
            AddRun(press, mold, daysAgo: i, good: 100, shots: 25, runMinutes: 52m, idealSeconds: 30m);
        var (press2, mold2) = await SeedAsync(moldShots: 0, code: "P2");
        for (var i = 1; i <= 4; i++)
            AddRun(press2, mold2, daysAgo: i, good: 100, shots: 25, runMinutes: 90m, idealSeconds: 30m);
        await _db.SaveChangesAsync();

        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);
        await _service.TriggerPredictionRunAsync(press2.Id, CancellationToken.None);

        (await _db.MaintenancePredictions.CountAsync(p => p.RuleKey == RuleBasedPredictiveMaintenanceService.CycleDriftRule)).Should().Be(0);
    }

    [Fact]
    public async Task Scrap_trend_fires_when_a_tools_scrap_rate_doubles()
    {
        var (press, mold) = await SeedAsync(moldShots: 0);
        AddRun(press, mold, daysAgo: 60, good: 990, shots: 250, scrap: 10);
        AddRun(press, mold, daysAgo: 4, good: 95, shots: 25, scrap: 5);
        AddRun(press, mold, daysAgo: 2, good: 96, shots: 25, scrap: 4);
        await _db.SaveChangesAsync();

        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        (await SingleAsync(RuleBasedPredictiveMaintenanceService.ScrapTrendRule)).PredictionType.Should().Be("RisingScrap");
    }

    [Fact]
    public async Task Scrap_trend_does_not_fire_on_a_steady_rate()
    {
        var (press, mold) = await SeedAsync(moldShots: 0);
        AddRun(press, mold, daysAgo: 60, good: 980, shots: 250, scrap: 20);
        AddRun(press, mold, daysAgo: 4, good: 98, shots: 25, scrap: 2);
        AddRun(press, mold, daysAgo: 2, good: 98, shots: 25, scrap: 2);
        await _db.SaveChangesAsync();

        await _service.TriggerPredictionRunAsync(press.Id, CancellationToken.None);

        (await _db.MaintenancePredictions.CountAsync(p => p.RuleKey == RuleBasedPredictiveMaintenanceService.ScrapTrendRule)).Should().Be(0);
    }

    [Fact]
    public async Task Trigger_all_runs_every_active_work_center_and_the_predictions_list_shows_the_rule_output()
    {
        var (press, mold) = await SeedAsync(moldShots: 4900);
        AddSchedule(mold, intervalShots: 5000);
        await _db.SaveChangesAsync();

        var checkedCount = await new TriggerAllPredictionRunsHandler(_db, _service)
            .Handle(new TriggerAllPredictionRunsCommand(), CancellationToken.None);
        var listed = await new GetPredictionsHandler(_db)
            .Handle(new GetPredictionsQuery(null, null, null), CancellationToken.None);

        checkedCount.Should().Be(1);
        listed.Should().ContainSingle();
        listed[0].AssetName.Should().Be(mold.Name);
        listed[0].WorkCenterName.Should().Be(press.Name);
        listed[0].RuleKey.Should().Be(RuleBasedPredictiveMaintenanceService.UsageRule);
    }

    [Fact]
    public async Task Daily_job_does_nothing_while_predictive_maintenance_is_off()
    {
        var capabilities = new Mock<Forge.Api.Capabilities.ICapabilitySnapshotProvider>();
        capabilities.Setup(c => c.IsEnabled("CAP-MAINT-PREDICTIVE")).Returns(false);
        var mediator = new Mock<MediatR.ISender>();

        await new Forge.Api.Jobs.PredictiveMaintenanceJob(
                mediator.Object, capabilities.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<Forge.Api.Jobs.PredictiveMaintenanceJob>.Instance)
            .RunPredictionsAsync(CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<TriggerAllPredictionRunsCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private async Task<MaintenancePrediction> SingleAsync(string rule) =>
        await _db.MaintenancePredictions.AsNoTracking().SingleAsync(p => p.RuleKey == rule);

    private async Task<(WorkCenter Press, Asset Mold)> SeedAsync(int moldShots, string code = "P1")
    {
        var press = new WorkCenter { Name = $"Press {code}", Code = code };
        var part = new Part { PartNumber = $"CAP-{code}", Name = "Cap" };
        _db.AddRange(press, part);
        await _db.SaveChangesAsync();
        var mold = new Asset
        {
            Name = $"Mold {code}", AssetType = AssetType.Tooling, CavityCount = 4, CurrentShotCount = moldShots, WorkCenterId = press.Id,
        };
        _db.Assets.Add(mold);
        await _db.SaveChangesAsync();
        var job = new Job { JobNumber = $"J-{code}", Title = "Caps", PartId = part.Id, TrackTypeId = 1, CurrentStageId = 1 };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        _jobs[press.Id] = (job.Id, part.Id);
        return (press, mold);
    }

    private readonly Dictionary<int, (int JobId, int PartId)> _jobs = [];

    private MaintenanceSchedule AddSchedule(Asset asset, int intervalShots)
    {
        var schedule = new MaintenanceSchedule
        {
            AssetId = asset.Id, Title = "Mold PM", IntervalDays = 365, IntervalShots = intervalShots, NextDueAt = Now.AddDays(200),
        };
        _db.MaintenanceSchedules.Add(schedule);
        return schedule;
    }

    private void AddRun(
        WorkCenter press, Asset mold, int daysAgo, int good, int shots,
        int scrap = 0, decimal? runMinutes = null, decimal? idealSeconds = null)
    {
        var (jobId, partId) = _jobs[press.Id];
        _db.ProductionRuns.Add(new ProductionRun
        {
            JobId = jobId,
            PartId = partId,
            WorkCenterId = press.Id,
            ToolingAssetId = mold.Id,
            RunNumber = $"RUN-{Guid.NewGuid():N}"[..14],
            TargetQuantity = good + scrap,
            CompletedQuantity = good,
            ScrapQuantity = scrap,
            ShotsApplied = shots,
            Status = ProductionRunStatus.Completed,
            CompletedAt = Now.AddDays(-daysAgo),
            RunTimeMinutes = runMinutes,
            IdealCycleTimeSeconds = idealSeconds,
        });
    }

    private void AddStop(Asset machine, int daysAgo, bool planned = false) =>
        _db.DowntimeLogs.Add(new DowntimeLog
        {
            AssetId = machine.Id,
            WorkCenterId = machine.WorkCenterId,
            StartedAt = Now.AddDays(-daysAgo),
            EndedAt = Now.AddDays(-daysAgo).AddMinutes(30),
            Reason = "Hydraulic fault",
            IsPlanned = planned,
        });
}
