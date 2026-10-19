using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Assets;
using Forge.Api.Features.Maintenance;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Assets;

[Collection(PostgresCollection.Name)]
public sealed class MaintenanceJobPostgresTests(PostgresFixture fixture)
{
    private static async Task EnsureMaintenanceTrackAsync(AppDbContext db)
    {
        var tracks = await db.TrackTypes
            .Include(t => t.Stages)
            .Where(t => t.Name.Contains("Maintenance"))
            .ToListAsync();
        if (tracks.Count == 0)
        {
            var track = new TrackType { Name = "Maintenance", Code = $"maint-{Guid.NewGuid():N}"[..20], IsActive = true };
            db.TrackTypes.Add(track);
            await db.SaveChangesAsync();
            tracks.Add(track);
        }
        foreach (var track in tracks.Where(t => !t.Stages.Any(s => s.IsActive)))
            db.JobStages.Add(new JobStage { TrackTypeId = track.Id, Name = "Requested", Code = "requested", SortOrder = 1, IsActive = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateMaintenanceJob_links_the_schedule_to_the_saved_job()
    {
        int scheduleId;
        await using (var seed = fixture.CreateContext())
        {
            await EnsureMaintenanceTrackAsync(seed);
            var asset = new Asset { Name = $"PM asset {Guid.NewGuid():N}"[..20], AssetType = AssetType.Machine };
            seed.Assets.Add(asset);
            await seed.SaveChangesAsync();
            var schedule = new MaintenanceSchedule
            {
                AssetId = asset.Id, Title = "Way cover inspection", IntervalDays = 30,
                NextDueAt = DateTimeOffset.UtcNow.AddDays(-2),
            };
            seed.MaintenanceSchedules.Add(schedule);
            await seed.SaveChangesAsync();
            scheduleId = schedule.Id;
        }

        await using var db = fixture.CreateContext();
        var handler = new CreateMaintenanceJobHandler(db, new JobRepository(db, new Forge.Integrations.SystemClock()));

        var jobId = await handler.Handle(new CreateMaintenanceJobCommand(scheduleId), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        jobId.Should().BeGreaterThan(0);
        var job = await verify.Jobs.Include(j => j.TrackType).SingleAsync(j => j.Id == jobId);
        job.TrackType.Name.Should().Contain("Maintenance");
        job.Title.Should().Contain("Way cover inspection");
        var linked = await verify.MaintenanceSchedules.SingleAsync(s => s.Id == scheduleId);
        linked.MaintenanceJobId.Should().Be(jobId);
    }

    [Fact]
    public async Task SchedulePreventiveMaintenance_creates_and_links_a_maintenance_job()
    {
        int predictionId;
        await using (var seed = fixture.CreateContext())
        {
            await EnsureMaintenanceTrackAsync(seed);
            var tag = Guid.NewGuid().ToString("N")[..6];
            var wc = new WorkCenter { Name = $"IMM {tag}", Code = $"IMM-{tag}" };
            seed.WorkCenters.Add(wc);
            await seed.SaveChangesAsync();
            var prediction = new MaintenancePrediction
            {
                WorkCenterId = wc.Id, PredictionType = "HydraulicPressureDrift", ConfidencePercent = 82.5m,
                PredictedFailureDate = DateTimeOffset.UtcNow.AddDays(9), ModelId = "pm-gbm", ModelVersion = "1",
                Status = MaintenancePredictionStatus.Acknowledged, Severity = MaintenancePredictionSeverity.High,
                PredictedAt = DateTimeOffset.UtcNow,
            };
            seed.MaintenancePredictions.Add(prediction);
            await seed.SaveChangesAsync();
            predictionId = prediction.Id;
        }

        await using var db = fixture.CreateContext();
        var handler = new SchedulePreventiveMaintenanceHandler(
            db, new JobRepository(db, new Forge.Integrations.SystemClock()), Mock.Of<IPredictiveMaintenanceService>());

        await handler.Handle(new SchedulePreventiveMaintenanceCommand(predictionId), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var saved = await verify.MaintenancePredictions.SingleAsync(p => p.Id == predictionId);
        saved.Status.Should().Be(MaintenancePredictionStatus.MaintenanceScheduled);
        saved.PreventiveMaintenanceJobId.Should().NotBeNull();
        var job = await verify.Jobs.SingleAsync(j => j.Id == saved.PreventiveMaintenanceJobId);
        job.Title.Should().Contain("HydraulicPressureDrift");

        await using var again = fixture.CreateContext();
        var repeat = new SchedulePreventiveMaintenanceHandler(
            again, new JobRepository(again, new Forge.Integrations.SystemClock()), Mock.Of<IPredictiveMaintenanceService>());
        var act = () => repeat.Handle(new SchedulePreventiveMaintenanceCommand(predictionId), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
