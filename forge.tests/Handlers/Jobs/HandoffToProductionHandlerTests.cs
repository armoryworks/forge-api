using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class HandoffToProductionHandlerTests
{
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly AppDbContext _db;
    private readonly HandoffToProductionHandler _handler;

    public HandoffToProductionHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("J-900");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _jobRepo.Setup(r => r.AddAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((job, _) => _db.Jobs.Add(job))
            .Returns(Task.CompletedTask);
        _handler = new HandoffToProductionHandler(_db, _jobRepo.Object);
    }

    [Fact]
    public async Task Handoff_FindsTheProductionTrackAfterItIsRenamed()
    {
        var production = await SeedTrackAsync("Shop Floor Orders", "production", isDefault: false);
        var rnd = await SeedTrackAsync("R&D/Tooling", "rnd", isDefault: false);
        var rdJob = await SeedJobAsync(rnd);

        var prodJobId = await _handler.Handle(new HandoffToProductionCommand(rdJob.Id), CancellationToken.None);

        var prodJob = await _db.Jobs.SingleAsync(j => j.Id == prodJobId);
        prodJob.TrackTypeId.Should().Be(production.Id);
        var links = await _db.Set<JobLink>().ToListAsync();
        links.Should().Contain(l => l.LinkType == JobLinkType.HandoffTo && l.SourceJobId == rdJob.Id && l.TargetJobId == prodJobId);
        links.Should().Contain(l => l.LinkType == JobLinkType.HandoffFrom && l.SourceJobId == prodJobId && l.TargetJobId == rdJob.Id);
        (await _db.JobActivityLogs.AnyAsync(l => l.JobId == prodJobId && l.Action == ActivityAction.Created)).Should().BeTrue();
        (await _db.JobActivityLogs.AnyAsync(l => l.JobId == rdJob.Id && l.Action == ActivityAction.HandedOff)).Should().BeTrue();
    }

    [Fact]
    public async Task Handoff_PrefersTheDefaultTrack()
    {
        await SeedTrackAsync("Production", "production", isDefault: false);
        var preferred = await SeedTrackAsync("Molding", "molding", isDefault: true);
        var rnd = await SeedTrackAsync("R&D/Tooling", "rnd", isDefault: false);
        var rdJob = await SeedJobAsync(rnd);

        var prodJobId = await _handler.Handle(new HandoffToProductionCommand(rdJob.Id), CancellationToken.None);

        (await _db.Jobs.SingleAsync(j => j.Id == prodJobId)).TrackTypeId.Should().Be(preferred.Id);
    }

    [Fact]
    public async Task Handoff_NeverPicksTheRdJobsOwnTrack()
    {
        var rnd = await SeedTrackAsync("R&D/Tooling", "rnd", isDefault: true);
        var rdJob = await SeedJobAsync(rnd);

        var act = () => _handler.Handle(new HandoffToProductionCommand(rdJob.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No production track is set. Mark one as the default in Admin > Order types.");
    }

    [Fact]
    public async Task Handoff_SkipsInactiveTracks()
    {
        var production = await SeedTrackAsync("Production", "production", isDefault: false);
        production.IsActive = false;
        var rnd = await SeedTrackAsync("R&D/Tooling", "rnd", isDefault: false);
        await _db.SaveChangesAsync();
        var rdJob = await SeedJobAsync(rnd);

        var act = () => _handler.Handle(new HandoffToProductionCommand(rdJob.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No production track is set.*");
    }

    [Fact]
    public async Task Handoff_StartsInTheFirstVisibleStatus()
    {
        var production = await SeedTrackAsync("Production", "production", isDefault: true);
        var hidden = await _db.JobStages.SingleAsync(s => s.TrackTypeId == production.Id);
        hidden.IsActive = false;
        var confirmed = new JobStage { TrackTypeId = production.Id, Name = "Order Confirmed", Code = "order_confirmed", SortOrder = 2 };
        _db.JobStages.Add(confirmed);
        var rnd = await SeedTrackAsync("R&D/Tooling", "rnd", isDefault: false);
        await _db.SaveChangesAsync();
        var rdJob = await SeedJobAsync(rnd);

        var prodJobId = await _handler.Handle(new HandoffToProductionCommand(rdJob.Id), CancellationToken.None);

        (await _db.Jobs.SingleAsync(j => j.Id == prodJobId)).CurrentStageId.Should().Be(confirmed.Id);
    }

    private async Task<TrackType> SeedTrackAsync(string name, string code, bool isDefault)
    {
        var track = new TrackType { Name = name, Code = code, IsDefault = isDefault, IsActive = true };
        _db.TrackTypes.Add(track);
        await _db.SaveChangesAsync();
        _db.JobStages.Add(new JobStage { TrackTypeId = track.Id, Name = "First", Code = $"{code}-1", SortOrder = 1 });
        await _db.SaveChangesAsync();
        return track;
    }

    private async Task<Job> SeedJobAsync(TrackType track)
    {
        var stage = await _db.JobStages.FirstAsync(s => s.TrackTypeId == track.Id);
        var job = new Job
        {
            JobNumber = "J-RD-1",
            Title = "Prototype fixture",
            TrackTypeId = track.Id,
            CurrentStageId = stage.Id,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }
}
