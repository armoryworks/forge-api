using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class JobPartMappingPostgresTests(PostgresFixture fixture)
{
    private static async Task<(TrackType Track, JobStage Stage, Part Part)> SeedAsync(Forge.Data.Context.AppDbContext db)
    {
        var track = new TrackType { Name = "JobPart-PG Track", Code = $"jp-pg-{Guid.NewGuid():N}"[..24], IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Stage 1", Code = "s1", SortOrder = 1, IsActive = true };
        var part = new Part { PartNumber = $"JP-{Guid.NewGuid():N}"[..16], Description = "JobPart mapping" };
        db.JobStages.Add(stage);
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return (track, stage, part);
    }

    [Fact]
    public async Task JobParts_AddedThroughTheJob_PersistAgainstThatJob()
    {
        int jobId;
        await using (var seed = fixture.CreateContext())
        {
            var (track, stage, part) = await SeedAsync(seed);
            var job = new Job
            {
                JobNumber = $"J-JP-{Guid.NewGuid():N}"[..14],
                Title = "Job with part",
                TrackTypeId = track.Id,
                CurrentStageId = stage.Id,
                PartId = part.Id,
            };
            job.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 500m });
            seed.Jobs.Add(job);
            await seed.SaveChangesAsync();
            jobId = job.Id;
        }

        await using var db = fixture.CreateContext();
        var stored = await db.JobParts.SingleAsync(jp => jp.JobId == jobId);
        stored.Quantity.Should().Be(500m);
    }

    [Fact]
    public async Task JobParts_AddedByJobId_LoadThroughTheJob()
    {
        int jobId;
        await using (var seed = fixture.CreateContext())
        {
            var (track, stage, part) = await SeedAsync(seed);
            var job = new Job
            {
                JobNumber = $"J-JP-{Guid.NewGuid():N}"[..14],
                Title = "Job with part",
                TrackTypeId = track.Id,
                CurrentStageId = stage.Id,
                PartId = part.Id,
            };
            seed.Jobs.Add(job);
            await seed.SaveChangesAsync();
            seed.JobParts.Add(new JobPart { JobId = job.Id, PartId = part.Id, Quantity = 250m });
            await seed.SaveChangesAsync();
            jobId = job.Id;
        }

        await using var db = fixture.CreateContext();
        var loaded = await db.Jobs.Include(j => j.JobParts).SingleAsync(j => j.Id == jobId);
        loaded.JobParts.Should().ContainSingle().Which.Quantity.Should().Be(250m);
    }
}
