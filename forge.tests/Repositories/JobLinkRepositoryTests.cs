using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs.Links;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class JobLinkRepositoryTests(PostgresFixture fixture)
{
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<(int First, int Second)> SeedTwinAsync(JobLinkType forward, JobLinkType backward)
    {
        await using var db = fixture.CreateContext();
        var track = new TrackType { Name = "Link twins", Code = Unique("link"), IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();

        var stage = new JobStage { TrackTypeId = track.Id, Name = "Open", Code = "open", SortOrder = 1, IsActive = true };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();

        var first = new Job { JobNumber = Unique("J"), Title = "First", TrackTypeId = track.Id, CurrentStageId = stage.Id };
        var second = new Job { JobNumber = Unique("J"), Title = "Second", TrackTypeId = track.Id, CurrentStageId = stage.Id };
        db.Jobs.AddRange(first, second);
        await db.SaveChangesAsync();

        db.JobLinks.Add(new JobLink { SourceJobId = first.Id, TargetJobId = second.Id, LinkType = forward });
        db.JobLinks.Add(new JobLink { SourceJobId = second.Id, TargetJobId = first.Id, LinkType = backward });
        await db.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    [Theory]
    [InlineData(JobLinkType.Parent, JobLinkType.Child)]
    [InlineData(JobLinkType.HandoffTo, JobLinkType.HandoffFrom)]
    public async Task A_twin_link_pair_is_listed_once_from_each_side(JobLinkType forward, JobLinkType backward)
    {
        var (first, second) = await SeedTwinAsync(forward, backward);

        await using var db = fixture.CreateContext();
        var repo = new JobLinkRepository(db);

        var fromFirst = await repo.GetByJobIdAsync(first, CancellationToken.None);
        var fromSecond = await repo.GetByJobIdAsync(second, CancellationToken.None);

        fromFirst.Should().ContainSingle();
        fromFirst[0].LinkedJobId.Should().Be(second);
        fromFirst[0].LinkType.Should().Be(forward.ToString());
        fromSecond.Should().ContainSingle();
        fromSecond[0].LinkedJobId.Should().Be(first);
        fromSecond[0].LinkType.Should().Be(backward.ToString());
    }

    [Fact]
    public async Task A_handoff_twin_counts_as_an_existing_link()
    {
        var (first, second) = await SeedTwinAsync(JobLinkType.HandoffTo, JobLinkType.HandoffFrom);

        await using var db = fixture.CreateContext();
        var repo = new JobLinkRepository(db);

        (await repo.LinkExistsAsync(second, first, JobLinkType.HandoffFrom, CancellationToken.None)).Should().BeTrue();
        (await repo.LinkExistsAsync(first, second, JobLinkType.HandoffTo, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task Deleting_one_side_of_a_twin_removes_the_relationship()
    {
        var (parent, child) = await SeedTwinAsync(JobLinkType.Parent, JobLinkType.Child);

        await using (var db = fixture.CreateContext())
        {
            var repo = new JobLinkRepository(db);
            var shown = (await repo.GetByJobIdAsync(parent, CancellationToken.None)).Single();

            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
            var hub = new Mock<IHubContext<BoardHub>>();
            hub.SetupGet(h => h.Clients).Returns(clients.Object);

            await new DeleteJobLinkHandler(repo, hub.Object)
                .Handle(new DeleteJobLinkCommand(parent, shown.Id), CancellationToken.None);
        }

        await using var verify = fixture.CreateContext();
        (await verify.JobLinks.AsNoTracking()
            .CountAsync(l => (l.SourceJobId == parent && l.TargetJobId == child)
                || (l.SourceJobId == child && l.TargetJobId == parent)))
            .Should().Be(0);
        (await new JobLinkRepository(verify).GetByJobIdAsync(child, CancellationToken.None)).Should().BeEmpty();
    }
}
