using FluentAssertions;

using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Moq;

using Forge.Api.Features.ShopFloor;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

[Collection(PostgresCollection.Name)]
public sealed class KioskJobsPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AvailableJobs_SearchByPartNumber_TranslatesAndCarriesQuantityAndStep()
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        int jobId;
        await using (var seed = fixture.CreateContext())
        {
            var (stage, part) = await SeedAsync(seed, tag);
            seed.Operations.Add(new Operation { PartId = part.Id, StepNumber = 10, Title = "Saw" });
            var job = NewJob(stage, $"KA-{tag}", part.Id);
            seed.Jobs.Add(job);
            await seed.SaveChangesAsync();
            seed.JobParts.Add(new JobPart { JobId = job.Id, PartId = part.Id, Quantity = 12m });
            await seed.SaveChangesAsync();
            jobId = job.Id;
        }

        await using var db = fixture.CreateContext();
        var result = await new GetKioskAvailableJobsHandler(db)
            .Handle(new GetKioskAvailableJobsQuery(null, $"pn-{tag}".ToUpperInvariant()), CancellationToken.None);

        var row = result.Should().ContainSingle().Subject;
        row.JobId.Should().Be(jobId);
        row.Quantity.Should().Be(12m);
        row.NextOperation!.Title.Should().Be("Saw");
    }

    [Fact]
    public async Task Claim_TwoWorkersAtOnce_OnlyOneWins()
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        int jobId, firstId, secondId;
        await using (var seed = fixture.CreateContext())
        {
            var (stage, _) = await SeedAsync(seed, tag);
            var job = NewJob(stage, $"KC-{tag}", null);
            var first = NewUser($"a{tag}");
            var second = NewUser($"b{tag}");
            seed.Jobs.Add(job);
            seed.Users.AddRange(first, second);
            await seed.SaveChangesAsync();
            (jobId, firstId, secondId) = (job.Id, first.Id, second.Id);
        }

        await using var dbA = fixture.CreateContext();
        await using var dbB = fixture.CreateContext();
        var outcomes = await Task.WhenAll(
            TryClaimAsync(dbA, jobId, firstId),
            TryClaimAsync(dbB, jobId, secondId));

        outcomes.Count(won => won).Should().Be(1);
        await using var check = fixture.CreateContext();
        (await check.Jobs.SingleAsync(j => j.Id == jobId)).AssigneeId.Should().BeOneOf(firstId, secondId);
        (await check.JobActivityLogs.CountAsync(l => l.JobId == jobId)).Should().Be(1);
    }

    private static async Task<bool> TryClaimAsync(AppDbContext db, int jobId, int userId)
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<BoardHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var handler = new ClaimKioskJobHandler(
            db, Mock.Of<IMediator>(), hub.Object, StubCapabilitySnapshotProvider.Off, clock.Object);

        try
        {
            await handler.Handle(new ClaimKioskJobCommand(jobId, userId), CancellationToken.None);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<(JobStage Stage, Part Part)> SeedAsync(AppDbContext seed, string tag)
    {
        var track = new TrackType { Name = $"Kiosk {tag}", Code = $"kiosk-{tag}", IsActive = true, IsShopFloor = true };
        seed.TrackTypes.Add(track);
        await seed.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Cut", Code = "cut", SortOrder = 1, IsActive = true, IsShopFloor = true };
        var part = new Part { PartNumber = $"PN-{tag}", Name = $"Part {tag}" };
        seed.JobStages.Add(stage);
        seed.Parts.Add(part);
        await seed.SaveChangesAsync();
        return (stage, part);
    }

    private static Job NewJob(JobStage stage, string jobNumber, int? partId) => new()
    {
        JobNumber = jobNumber,
        Title = jobNumber,
        TrackTypeId = stage.TrackTypeId,
        CurrentStageId = stage.Id,
        PartId = partId,
    };

    private static ApplicationUser NewUser(string handle) => new()
    {
        FirstName = handle,
        LastName = "Kiosk",
        UserName = $"{handle}@example.com",
        Email = $"{handle}@example.com",
        IsActive = true,
    };
}
