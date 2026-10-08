using FluentAssertions;

using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.ShopFloor;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class ClaimKioskJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly Mock<IClientProxy> _proxy = new();
    private readonly Mock<IHubClients> _clients = new();
    private readonly Mock<IClock> _clock = new();
    private JobStage _floor = null!;
    private JobStage _office = null!;

    public ClaimKioskJobTests()
    {
        _clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_proxy.Object);
        _boardHub.Setup(h => h.Clients).Returns(_clients.Object);
        _clock.Setup(c => c.UtcNow).Returns(Now);
    }

    [Fact]
    public async Task Handle_UnassignedOpenJob_AssignsLogsAndBroadcasts()
    {
        await SeedStagesAsync();
        var worker = await AddUserAsync("Pat", "Lathe");
        var job = await AddJobAsync("J-1");

        await Handler().Handle(new ClaimKioskJobCommand(job.Id, worker.Id), CancellationToken.None);

        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).AssigneeId.Should().Be(worker.Id);
        var log = await _db.JobActivityLogs.SingleAsync(l => l.JobId == job.Id);
        log.Action.Should().Be(ActivityAction.Assigned);
        log.UserId.Should().Be(worker.Id);
        log.NewValue.Should().Be("Lathe, Pat");
        log.CreatedAt.Should().Be(Now);
        _mediator.Verify(m => m.Send(It.Is<GetJobByIdQuery>(q => q.Id == job.Id), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(c => c.Group($"board:{job.TrackTypeId}"), Times.Once);
        _clients.Verify(c => c.Group($"job:{job.Id}"), Times.Once);
        _proxy.Verify(p => p.SendCoreAsync("jobUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_SecondClaim_IsRefusedAndKeepsTheFirstWorker()
    {
        await SeedStagesAsync();
        var first = await AddUserAsync("First", "Worker");
        var second = await AddUserAsync("Second", "Worker");
        var job = await AddJobAsync("J-1");
        await Handler().Handle(new ClaimKioskJobCommand(job.Id, first.Id), CancellationToken.None);

        var act = () => Handler().Handle(new ClaimKioskJobCommand(job.Id, second.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already assigned*");
        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).AssigneeId.Should().Be(first.Id);
        (await _db.JobActivityLogs.CountAsync(l => l.JobId == job.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_JobAtOfficeStatus_IsRefused()
    {
        await SeedStagesAsync();
        var worker = await AddUserAsync("Pat", "Lathe");
        var job = await AddJobAsync("J-1", stage: _office);

        var act = () => Handler().Handle(new ClaimKioskJobCommand(job.Id, worker.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not open at a shop-floor status*");
        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).AssigneeId.Should().BeNull();
    }

    [Theory]
    [InlineData("archived")]
    [InlineData("completed")]
    [InlineData("disposed")]
    public async Task Handle_ClosedJob_IsRefused(string state)
    {
        await SeedStagesAsync();
        var worker = await AddUserAsync("Pat", "Lathe");
        var job = await AddJobAsync("J-1", j =>
        {
            switch (state)
            {
                case "archived": j.IsArchived = true; break;
                case "completed": j.CompletedDate = Now.AddDays(-1); break;
                default: j.Disposition = JobDisposition.Scrap; break;
            }
        });

        var act = () => Handler().Handle(new ClaimKioskJobCommand(job.Id, worker.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _proxy.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownJob_ThrowsNotFound()
    {
        await SeedStagesAsync();
        var worker = await AddUserAsync("Pat", "Lathe");

        var act = () => Handler().Handle(new ClaimKioskJobCommand(999, worker.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_InactiveWorker_IsRefused()
    {
        await SeedStagesAsync();
        var worker = await AddUserAsync("Pat", "Lathe");
        worker.IsActive = false;
        await _db.SaveChangesAsync();
        var job = await AddJobAsync("J-1");

        var act = () => Handler().Handle(new ClaimKioskJobCommand(job.Id, worker.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).AssigneeId.Should().BeNull();
    }

    private ClaimKioskJobHandler Handler() =>
        new(_db, _mediator.Object, _boardHub.Object, StubCapabilitySnapshotProvider.Off, _clock.Object);

    private async Task SeedStagesAsync()
    {
        var track = new TrackType { Name = "Production", Code = "production", IsDefault = true, IsShopFloor = true };
        _floor = new JobStage { Name = "Machining", Code = "machining", SortOrder = 1, IsShopFloor = true, TrackType = track };
        _office = new JobStage { Name = "Quoting", Code = "quoting", SortOrder = 0, IsShopFloor = false, TrackType = track };
        _db.TrackTypes.Add(track);
        _db.JobStages.AddRange(_floor, _office);
        await _db.SaveChangesAsync();
    }

    private async Task<ApplicationUser> AddUserAsync(string first, string last)
    {
        var email = $"{first}.{last}@example.com".ToLowerInvariant();
        var user = new ApplicationUser
        {
            FirstName = first,
            LastName = last,
            UserName = email,
            Email = email,
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private Task<Job> AddJobAsync(string jobNumber, Action<Job> configure) =>
        AddJobAsync(jobNumber, null, configure);

    private async Task<Job> AddJobAsync(string jobNumber, JobStage? stage = null, Action<Job>? configure = null)
    {
        var target = stage ?? _floor;
        var job = new Job
        {
            JobNumber = jobNumber,
            Title = jobNumber,
            TrackTypeId = target.TrackTypeId,
            CurrentStageId = target.Id,
        };
        configure?.Invoke(job);
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }
}
