using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

[Collection(PostgresCollection.Name)]
public sealed class HandoffToProductionPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Handoff_persists_links_to_the_new_job_and_skips_the_rd_track()
    {
        int rdJobId, rdTrackId;

        await using (var seed = fixture.CreateContext())
        {
            var production = await seed.TrackTypes
                .Include(t => t.Stages)
                .FirstOrDefaultAsync(t => t.Code == "production");
            if (production is null)
            {
                production = new TrackType { Name = "Renamed Shop Orders", Code = "production", IsActive = true };
                seed.TrackTypes.Add(production);
                await seed.SaveChangesAsync();
            }
            production.IsActive = true;
            if (production.Stages.Count == 0)
                seed.JobStages.Add(new JobStage { TrackTypeId = production.Id, Name = "Queued", Code = "queued", SortOrder = 1, IsActive = true });

            var rnd = new TrackType { Name = "Handoff-PG R&D", Code = $"handoff-rd-{Guid.NewGuid():N}"[..24], IsActive = true };
            seed.TrackTypes.Add(rnd);
            await seed.SaveChangesAsync();

            var rdStage = new JobStage { TrackTypeId = rnd.Id, Name = "Design", Code = "design", SortOrder = 1, IsActive = true };
            seed.JobStages.Add(rdStage);
            await seed.SaveChangesAsync();

            var rdJob = new Job
            {
                JobNumber = $"J-HO-{Guid.NewGuid():N}"[..12],
                Title = "Handoff R&D job",
                TrackTypeId = rnd.Id,
                CurrentStageId = rdStage.Id,
            };
            seed.Jobs.Add(rdJob);
            await seed.SaveChangesAsync();

            rdJobId = rdJob.Id;
            rdTrackId = rnd.Id;
        }

        await using var db = fixture.CreateContext();
        var handler = new HandoffToProductionHandler(db, new JobRepository(db, new Forge.Integrations.SystemClock()));

        var prodJobId = await handler.Handle(new HandoffToProductionCommand(rdJobId), CancellationToken.None);

        prodJobId.Should().BeGreaterThan(0);

        await using var verify = fixture.CreateContext();
        var prodJob = await verify.Jobs.SingleAsync(j => j.Id == prodJobId);
        prodJob.TrackTypeId.Should().NotBe(rdTrackId);

        var links = await verify.Set<JobLink>()
            .Where(l => l.SourceJobId == rdJobId || l.TargetJobId == rdJobId)
            .ToListAsync();
        links.Should().Contain(l => l.LinkType == JobLinkType.HandoffTo && l.TargetJobId == prodJobId);
        links.Should().Contain(l => l.LinkType == JobLinkType.HandoffFrom && l.SourceJobId == prodJobId);
    }
}
