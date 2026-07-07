using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

/// <summary>
/// Regression for the QA-reported "Explode BOM does nothing": the handler built
/// JobLink/JobPart rows from <c>childJob.Id</c> BEFORE SaveChanges assigned it,
/// so on real Postgres every Make-line explosion died with an FK violation
/// (target_job_id = 0) and the response models carried id 0. The InMemory
/// provider + mocked repository could not reproduce either failure — this must
/// run against REAL Postgres with the real JobRepository.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExplodeJobBomPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Explode_persists_child_job_links_and_returns_real_ids()
    {
        int parentJobId, childPartId;

        await using (var seed = fixture.CreateContext())
        {
            var track = new TrackType { Name = "Explode-PG Track", Code = $"explode-pg-{Guid.NewGuid():N}"[..24], IsActive = true };
            seed.TrackTypes.Add(track);
            await seed.SaveChangesAsync();

            var stage = new JobStage { TrackTypeId = track.Id, Name = "Stage 1", Code = "s1", SortOrder = 1, IsActive = true };
            seed.JobStages.Add(stage);

            var parentPart = new Part { PartNumber = $"EXPL-P-{Guid.NewGuid():N}"[..16], Description = "Explode parent" };
            var childPart = new Part { PartNumber = $"EXPL-C-{Guid.NewGuid():N}"[..16], Description = "Explode child" };
            seed.Parts.AddRange(parentPart, childPart);
            await seed.SaveChangesAsync();

            seed.BOMLines.Add(new BOMLine
            {
                ParentPartId = parentPart.Id,
                ChildPartId = childPart.Id,
                Quantity = 2,
                SourceType = BOMSourceType.Make,
                SortOrder = 1,
            });

            var parentJob = new Job
            {
                JobNumber = $"J-EXPL-{Guid.NewGuid():N}"[..14],
                Title = "Explode parent job",
                TrackTypeId = track.Id,
                CurrentStageId = stage.Id,
                PartId = parentPart.Id,
            };
            seed.Jobs.Add(parentJob);
            await seed.SaveChangesAsync();

            parentJobId = parentJob.Id;
            childPartId = childPart.Id;
        }

        await using var db = fixture.CreateContext();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<BoardHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);

        var handler = new ExplodeJobBomHandler(
            db, new JobRepository(db), Mock.Of<IBarcodeService>(), hub.Object);

        // Before the fix this threw DbUpdateException (FK violation on job_links).
        var result = await handler.Handle(new ExplodeJobBomCommand(parentJobId), CancellationToken.None);

        result.CreatedJobs.Should().ContainSingle();
        result.CreatedJobs[0].JobId.Should().BeGreaterThan(0,
            "the response must carry the database-assigned child id, not the pre-save 0");
        result.CreatedJobs[0].PartId.Should().Be(childPartId);

        await using var verify = fixture.CreateContext();
        var childJob = await verify.Jobs.SingleAsync(j => j.Id == result.CreatedJobs[0].JobId);
        childJob.ParentJobId.Should().Be(parentJobId);
        childJob.PartId.Should().Be(childPartId);

        var links = await verify.Set<JobLink>()
            .Where(l => l.SourceJobId == parentJobId || l.TargetJobId == parentJobId)
            .ToListAsync();
        links.Should().HaveCount(2);
        links.Should().Contain(l => l.LinkType == JobLinkType.Parent && l.TargetJobId == childJob.Id);
        links.Should().Contain(l => l.LinkType == JobLinkType.Child && l.SourceJobId == childJob.Id);

        var jobPart = await verify.Set<JobPart>().SingleAsync(p => p.JobId == childJob.Id);
        jobPart.PartId.Should().Be(childPartId);
        jobPart.Quantity.Should().Be(2);
    }
}
