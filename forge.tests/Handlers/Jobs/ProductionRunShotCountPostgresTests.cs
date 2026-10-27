using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Assets;
using Forge.Api.Features.Jobs.ProductionRuns;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

[Collection(PostgresCollection.Name)]
public sealed class ProductionRunShotCountPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Run_on_a_four_cavity_mold_adds_shots_once_and_corrections_apply_only_the_delta()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int jobId, partId, moldId, scheduleId;
        await using (var seed = fixture.CreateContext())
        {
            var track = new TrackType { Name = $"Shots {suffix}", Code = $"shot-{suffix}", IsActive = true };
            seed.TrackTypes.Add(track);
            await seed.SaveChangesAsync();
            var stage = new JobStage { TrackTypeId = track.Id, Name = "Stage 1", Code = "s1", SortOrder = 1, IsActive = true };
            seed.JobStages.Add(stage);
            var mold = new Asset { Name = $"Mold {suffix}", AssetType = AssetType.Tooling, CavityCount = 4, CurrentShotCount = 0 };
            seed.Assets.Add(mold);
            await seed.SaveChangesAsync();
            var part = new Part { PartNumber = $"CAP-{suffix}", Name = "Cap", ToolingAssetId = mold.Id };
            seed.Parts.Add(part);
            await seed.SaveChangesAsync();
            var job = new Job
            {
                JobNumber = $"J-SH-{suffix}", Title = "Caps", TrackTypeId = track.Id, CurrentStageId = stage.Id, PartId = part.Id,
            };
            seed.Jobs.Add(job);
            var pm = new MaintenanceSchedule
            {
                AssetId = mold.Id, Title = "Mold PM", IntervalDays = 365, IntervalShots = 500,
                NextDueAt = DateTimeOffset.UtcNow.AddDays(300),
            };
            seed.MaintenanceSchedules.Add(pm);
            await seed.SaveChangesAsync();
            (jobId, partId, moldId, scheduleId) = (job.Id, part.Id, mold.Id, pm.Id);
        }

        var clock = new Forge.Integrations.SystemClock();
        int runId;
        await using (var db = fixture.CreateContext())
        {
            var created = await new CreateProductionRunHandler(db, clock)
                .Handle(new CreateProductionRunCommand(jobId, partId, 2000, null, null), CancellationToken.None);
            created.ToolingAssetId.Should().Be(moldId, "the run defaults to the part's tooling");
            runId = created.Id;
        }

        await UpdateAsync(jobId, runId, 1000, 0, "InProgress");
        (await ShotsAsync(moldId)).Should().Be(250);

        var response = await UpdateAsync(jobId, runId, 1960, 40, "Completed");
        response.ShotsApplied.Should().Be(500);
        (await ShotsAsync(moldId)).Should().Be(500, "2,000 parts on a 4-cavity mold is 500 shots, added once");

        await UpdateAsync(jobId, runId, 1960, 40, "Completed");
        (await ShotsAsync(moldId)).Should().Be(500, "re-saving the same quantities adds nothing");

        await UpdateAsync(jobId, runId, 1900, 40, "Completed");
        (await ShotsAsync(moldId)).Should().Be(485, "a correction applies only the difference");

        await UpdateAsync(jobId, runId, 1960, 40, "Completed");

        await using var verify = fixture.CreateContext();
        var asset = await verify.Assets.AsNoTracking().SingleAsync(a => a.Id == moldId);
        var schedule = await verify.MaintenanceSchedules.AsNoTracking().SingleAsync(s => s.Id == scheduleId);
        var last = await MaintenanceUsage.LastServiceReadingsAsync(verify, [scheduleId], CancellationToken.None);
        MaintenanceUsage.IsDueByShots(schedule, asset, last.GetValueOrDefault(scheduleId)).Should().BeTrue();
        MaintenanceUsage.IsDue(schedule, asset, last.GetValueOrDefault(scheduleId), DateTimeOffset.UtcNow).Should().BeTrue();
        (await verify.ProductionRuns.AsNoTracking().SingleAsync(r => r.Id == runId)).ShotsApplied.Should().Be(500);
    }

    private async Task<Forge.Core.Models.ProductionRunResponseModel> UpdateAsync(
        int jobId, int runId, int good, int scrap, string status)
    {
        await using var db = fixture.CreateContext();
        return await new UpdateProductionRunHandler(db, new Forge.Integrations.SystemClock())
            .Handle(new UpdateProductionRunCommand(jobId, runId, good, scrap, status, null, null, null), CancellationToken.None);
    }

    private async Task<int> ShotsAsync(int assetId)
    {
        await using var db = fixture.CreateContext();
        return (await db.Assets.AsNoTracking().SingleAsync(a => a.Id == assetId)).CurrentShotCount;
    }
}
