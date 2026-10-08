using FluentAssertions;

using Microsoft.AspNetCore.Identity;

using Moq;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class KioskAvailableJobsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IClockEventTypeService> _eventTypes = new();
    private JobStage _floor = null!;
    private JobStage _office = null!;

    public KioskAvailableJobsTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _eventTypes.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyUnassignedOpenJobsAtShopFloorStatuses()
    {
        await SeedStagesAsync();
        var worker = await AddUserAsync();
        await AddJobAsync("J-OPEN");
        await AddJobAsync("J-TAKEN", assigneeId: worker.Id);
        await AddJobAsync("J-OFFICE", stage: _office);
        await AddJobAsync("J-ARCHIVED", configure: j => j.IsArchived = true);
        await AddJobAsync("J-DONE", configure: j => j.CompletedDate = Now.AddDays(-1));
        await AddJobAsync("J-SCRAP", configure: j => j.Disposition = JobDisposition.Scrap);

        var result = await Handler().Handle(new GetKioskAvailableJobsQuery(null, null), CancellationToken.None);

        result.Select(j => j.JobNumber).Should().Equal("J-OPEN");
    }

    [Theory]
    [InlineData("j-100", "J-100")]
    [InlineData("bracket", "J-200")]
    [InlineData("pn-77", "J-300")]
    public async Task Handle_Search_MatchesJobNumberTitleOrPartNumber(string search, string expected)
    {
        await SeedStagesAsync();
        await AddJobAsync("J-100", title: "Housing");
        await AddJobAsync("J-200", title: "Mounting Bracket");
        var part = await AddPartAsync("PN-7788");
        await AddJobAsync("J-300", title: "Spacer", partId: part.Id);

        var result = await Handler().Handle(new GetKioskAvailableJobsQuery(null, search), CancellationToken.None);

        result.Select(j => j.JobNumber).Should().Equal(expected);
    }

    [Fact]
    public async Task Handle_SortsByDueDateThenPriorityWithUndatedLast()
    {
        await SeedStagesAsync();
        await AddJobAsync("J-UNDATED", priority: JobPriority.Urgent);
        await AddJobAsync("J-LATE-NORMAL", dueDate: Now.AddDays(5), priority: JobPriority.Normal);
        await AddJobAsync("J-LATE-URGENT", dueDate: Now.AddDays(5), priority: JobPriority.Urgent);
        await AddJobAsync("J-SOON", dueDate: Now.AddDays(1), priority: JobPriority.Low);

        var result = await Handler().Handle(new GetKioskAvailableJobsQuery(null, null), CancellationToken.None);

        result.Select(j => j.JobNumber).Should().Equal("J-SOON", "J-LATE-URGENT", "J-LATE-NORMAL", "J-UNDATED");
    }

    [Fact]
    public async Task Handle_TakeLimitsTheRows()
    {
        await SeedStagesAsync();
        for (var i = 1; i <= 5; i++)
            await AddJobAsync($"J-{i}", dueDate: Now.AddDays(i));

        var result = await Handler().Handle(new GetKioskAvailableJobsQuery(null, null, 2), CancellationToken.None);

        result.Select(j => j.JobNumber).Should().Equal("J-1", "J-2");
    }

    [Fact]
    public async Task Handle_RowCarriesPartQuantityAndFirstRoutingStep()
    {
        await SeedStagesAsync();
        var part = await AddPartAsync("PN-1");
        var lathe = new WorkCenter { Name = "Lathe", Code = "LATHE" };
        _db.WorkCenters.Add(lathe);
        _db.Operations.AddRange(
            new Operation { PartId = part.Id, StepNumber = 20, Title = "Deburr" },
            new Operation { PartId = part.Id, StepNumber = 10, Title = "Turn", WorkCenter = lathe });
        await _db.SaveChangesAsync();
        var job = await AddJobAsync("J-PART", partId: part.Id, dueDate: Now.AddDays(2));
        _db.JobParts.Add(new JobPart { JobId = job.Id, PartId = part.Id, Quantity = 25m });
        await AddJobAsync("J-BARE");
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(new GetKioskAvailableJobsQuery(null, null), CancellationToken.None);

        var withPart = result.Single(j => j.JobNumber == "J-PART");
        withPart.PartNumber.Should().Be("PN-1");
        withPart.Quantity.Should().Be(25m);
        withPart.DueDate.Should().Be(Now.AddDays(2));
        withPart.NextOperation.Should().NotBeNull();
        withPart.NextOperation!.StepNumber.Should().Be(10);
        withPart.NextOperation.Title.Should().Be("Turn");
        withPart.NextOperation.WorkCenterId.Should().Be(lathe.Id);
        withPart.NextOperation.WorkCenterName.Should().Be("Lathe");

        var bare = result.Single(j => j.JobNumber == "J-BARE");
        bare.PartNumber.Should().BeNull();
        bare.Quantity.Should().Be(1m);
        bare.NextOperation.Should().BeNull();
    }

    [Fact]
    public async Task Overview_ReadyToStartCount_CountsUnassignedOpenShopFloorJobs()
    {
        await SeedStagesAsync();
        var worker = await AddUserAsync();
        await AddJobAsync("J-1");
        await AddJobAsync("J-2");
        await AddJobAsync("J-TAKEN", assigneeId: worker.Id);
        await AddJobAsync("J-OFFICE", stage: _office);
        await AddJobAsync("J-ARCHIVED", configure: j => j.IsArchived = true);

        var result = await new GetShopFloorOverviewHandler(_db, _eventTypes.Object, _clock.Object)
            .Handle(new GetShopFloorOverviewQuery(7), CancellationToken.None);

        result.ReadyToStartCount.Should().Be(2);
    }

    [Fact]
    public async Task ClockStatus_Assignment_CarriesTimerStartPartAndNextOperation()
    {
        await SeedStagesAsync();
        var worker = await AddUserAsync();
        var part = await AddPartAsync("PN-9");
        _db.Operations.Add(new Operation { PartId = part.Id, StepNumber = 10, Title = "Mill" });
        await _db.SaveChangesAsync();
        var running = await AddJobAsync("J-RUN", assigneeId: worker.Id, partId: part.Id);
        await AddJobAsync("J-IDLE", assigneeId: worker.Id);
        var timerStart = Now.AddMinutes(-42);
        _db.TimeEntries.Add(new TimeEntry
        {
            JobId = running.Id,
            UserId = worker.Id,
            Date = DateOnly.FromDateTime(Now.UtcDateTime),
            TimerStart = timerStart,
        });
        await _db.SaveChangesAsync();

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var result = await new GetClockStatusHandler(_db, userManager.Object, _eventTypes.Object, _clock.Object)
            .Handle(new GetClockStatusQuery(), CancellationToken.None);

        var assignments = result.Single().Assignments;
        var run = assignments.Single(a => a.JobNumber == "J-RUN");
        run.HasActiveTimer.Should().BeTrue();
        run.TimerStartedAt.Should().Be(timerStart);
        run.PartNumber.Should().Be("PN-9");
        run.NextOperation!.Title.Should().Be("Mill");

        var idle = assignments.Single(a => a.JobNumber == "J-IDLE");
        idle.HasActiveTimer.Should().BeFalse();
        idle.TimerStartedAt.Should().BeNull();
        idle.PartNumber.Should().BeNull();
        idle.NextOperation.Should().BeNull();
    }

    private GetKioskAvailableJobsHandler Handler() => new(_db);

    private async Task SeedStagesAsync()
    {
        var track = new TrackType { Name = "Production", Code = "production", IsDefault = true, IsShopFloor = true };
        _floor = new JobStage { Name = "Machining", Code = "machining", SortOrder = 1, IsShopFloor = true, TrackType = track };
        _office = new JobStage { Name = "Quoting", Code = "quoting", SortOrder = 0, IsShopFloor = false, TrackType = track };
        _db.TrackTypes.Add(track);
        _db.JobStages.AddRange(_floor, _office);
        await _db.SaveChangesAsync();
    }

    private async Task<ApplicationUser> AddUserAsync()
    {
        var user = new ApplicationUser
        {
            FirstName = "Floor",
            LastName = "Worker",
            UserName = "floor.worker@example.com",
            Email = "floor.worker@example.com",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Part> AddPartAsync(string partNumber)
    {
        var part = new Part { PartNumber = partNumber, Name = partNumber };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<Job> AddJobAsync(
        string jobNumber,
        string? title = null,
        JobStage? stage = null,
        int? assigneeId = null,
        int? partId = null,
        DateTimeOffset? dueDate = null,
        JobPriority priority = JobPriority.Normal,
        Action<Job>? configure = null)
    {
        var target = stage ?? _floor;
        var job = new Job
        {
            JobNumber = jobNumber,
            Title = title ?? jobNumber,
            TrackTypeId = target.TrackTypeId,
            CurrentStageId = target.Id,
            AssigneeId = assigneeId,
            PartId = partId,
            DueDate = dueDate,
            Priority = priority,
        };
        configure?.Invoke(job);
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }
}
