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
    private readonly Mock<IJobOperationService> _operations = new();
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
    public async Task Overview_ReadyToStartCount_TeamOwningNoWorkCenters_CountsAllUnassignedOpenShopFloorJobs()
    {
        await SeedStagesAsync();
        var worker = await AddUserAsync();
        var team = await AddTeamAsync("Assembly");
        await AddJobAsync("J-1");
        await AddJobAsync("J-2");
        await AddJobAsync("J-TAKEN", assigneeId: worker.Id);
        await AddJobAsync("J-OFFICE", stage: _office);
        await AddJobAsync("J-ARCHIVED", configure: j => j.IsArchived = true);

        var result = await OverviewHandler().Handle(new GetShopFloorOverviewQuery(team.Id), CancellationToken.None);

        result.ReadyToStartCount.Should().Be(2);
    }

    [Fact]
    public async Task TeamOwningWorkCenters_SeesOnlyJobsWhoseNextStepIsAtItsWorkCenters()
    {
        await SeedStagesAsync();
        var (machining, welding) = (await AddTeamAsync("Machining"), await AddTeamAsync("Welding"));
        var mill = await AddWorkCenterAsync("Mill", machining.Id);
        var welder = await AddWorkCenterAsync("Welder", welding.Id);
        var millFirst = await AddPartAsync("PN-MILL");
        await AddStepAsync(millFirst, 10, "Mill", mill);
        await AddStepAsync(millFirst, 20, "Weld", welder);
        var weldFirst = await AddPartAsync("PN-WELD");
        await AddStepAsync(weldFirst, 10, "Weld", welder);
        await AddStepAsync(weldFirst, 20, "Mill", mill);
        await AddJobAsync("J-MILL", partId: millFirst.Id);
        await AddJobAsync("J-WELD", partId: weldFirst.Id);
        await AddJobAsync("J-BARE");

        var machiningJobs = await Handler().Handle(
            new GetKioskAvailableJobsQuery(machining.Id, null), CancellationToken.None);
        var weldingJobs = await Handler().Handle(
            new GetKioskAvailableJobsQuery(welding.Id, null), CancellationToken.None);
        var unscoped = await Handler().Handle(new GetKioskAvailableJobsQuery(null, null), CancellationToken.None);
        var overview = await OverviewHandler().Handle(
            new GetShopFloorOverviewQuery(machining.Id), CancellationToken.None);

        machiningJobs.Select(j => j.JobNumber).Should().Equal("J-MILL");
        weldingJobs.Select(j => j.JobNumber).Should().Equal("J-WELD");
        unscoped.Select(j => j.JobNumber).Should().BeEquivalentTo("J-MILL", "J-WELD", "J-BARE");
        overview.ReadyToStartCount.Should().Be(1);
    }

    [Fact]
    public async Task TeamFilter_WithOperationTracking_FollowsTheFirstOpenStep()
    {
        await SeedStagesAsync();
        EnableTracking();
        var (machining, welding) = (await AddTeamAsync("Machining"), await AddTeamAsync("Welding"));
        var mill = await AddWorkCenterAsync("Mill", machining.Id);
        var welder = await AddWorkCenterAsync("Welder", welding.Id);
        var part = await AddPartAsync("PN-1");
        var first = await AddStepAsync(part, 10, "Mill", mill);
        await AddStepAsync(part, 20, "Weld", welder);
        var job = await AddJobAsync("J-MILLED", partId: part.Id);
        await AddJobOperationAsync(job, first, JobOperationStatus.Complete);

        var machiningJobs = await Handler().Handle(
            new GetKioskAvailableJobsQuery(machining.Id, null), CancellationToken.None);
        var weldingJobs = await Handler().Handle(
            new GetKioskAvailableJobsQuery(welding.Id, null), CancellationToken.None);

        machiningJobs.Should().BeEmpty();
        var row = weldingJobs.Should().ContainSingle().Subject;
        row.NextOperation!.StepNumber.Should().Be(20);
        row.NextOperation.WorkCenterName.Should().Be("Welder");
    }

    [Theory]
    [InlineData(JobOperationStatus.Complete)]
    [InlineData(JobOperationStatus.Skipped)]
    public async Task ClockStatus_WithOperationTracking_NextOperationSkipsClosedSteps(JobOperationStatus closed)
    {
        await SeedStagesAsync();
        EnableTracking();
        var worker = await AddUserAsync();
        var part = await AddPartAsync("PN-2");
        var saw = await AddStepAsync(part, 10, "Saw");
        await AddStepAsync(part, 20, "Drill");
        var job = await AddJobAsync("J-AT-DRILL", assigneeId: worker.Id, partId: part.Id);
        await AddJobOperationAsync(job, saw, closed);

        var result = await ClockStatusHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        var assignment = result.Single().Assignments.Single();
        assignment.NextOperation!.StepNumber.Should().Be(20);
        assignment.NextOperation.Title.Should().Be("Drill");
    }

    [Fact]
    public async Task ClockStatus_WithOperationTracking_PrefersAStepInProgress()
    {
        await SeedStagesAsync();
        EnableTracking();
        var worker = await AddUserAsync();
        var part = await AddPartAsync("PN-3");
        await AddStepAsync(part, 10, "Saw");
        var drill = await AddStepAsync(part, 20, "Drill");
        var job = await AddJobAsync("J-DRILLING", assigneeId: worker.Id, partId: part.Id);
        await AddJobOperationAsync(job, drill, JobOperationStatus.InProgress);

        var result = await ClockStatusHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        result.Single().Assignments.Single().NextOperation!.Title.Should().Be("Drill");
    }

    [Fact]
    public async Task ClockStatus_AllStepsClosed_HasNoNextOperation()
    {
        await SeedStagesAsync();
        EnableTracking();
        var worker = await AddUserAsync();
        var part = await AddPartAsync("PN-4");
        var only = await AddStepAsync(part, 10, "Pack");
        var job = await AddJobAsync("J-PACKED", assigneeId: worker.Id, partId: part.Id);
        await AddJobOperationAsync(job, only, JobOperationStatus.Complete);

        var result = await ClockStatusHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        result.Single().Assignments.Single().NextOperation.Should().BeNull();
    }

    [Fact]
    public async Task ClockStatus_WithoutOperationTracking_ReportsTheFirstRoutingStep()
    {
        await SeedStagesAsync();
        var worker = await AddUserAsync();
        var part = await AddPartAsync("PN-5");
        var saw = await AddStepAsync(part, 10, "Saw");
        await AddStepAsync(part, 20, "Drill");
        var job = await AddJobAsync("J-UNTRACKED", assigneeId: worker.Id, partId: part.Id);
        await AddJobOperationAsync(job, saw, JobOperationStatus.Complete);

        var result = await ClockStatusHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        result.Single().Assignments.Single().NextOperation!.Title.Should().Be("Saw");
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

        var result = await ClockStatusHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

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

    private GetKioskAvailableJobsHandler Handler() => new(_db, _operations.Object);

    private GetShopFloorOverviewHandler OverviewHandler() =>
        new(_db, _eventTypes.Object, _operations.Object, _clock.Object);

    private GetClockStatusHandler ClockStatusHandler()
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        return new GetClockStatusHandler(_db, userManager.Object, _eventTypes.Object, _operations.Object, _clock.Object);
    }

    private void EnableTracking() =>
        _operations.Setup(o => o.IsTrackingEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

    private async Task<Team> AddTeamAsync(string name)
    {
        var team = new Team { Name = name };
        _db.Teams.Add(team);
        await _db.SaveChangesAsync();
        return team;
    }

    private async Task<WorkCenter> AddWorkCenterAsync(string name, int? teamId)
    {
        var workCenter = new WorkCenter { Name = name, Code = name.ToUpperInvariant(), TeamId = teamId };
        _db.WorkCenters.Add(workCenter);
        await _db.SaveChangesAsync();
        return workCenter;
    }

    private async Task<Operation> AddStepAsync(Part part, int stepNumber, string title, WorkCenter? workCenter = null)
    {
        var operation = new Operation
        {
            PartId = part.Id,
            StepNumber = stepNumber,
            Title = title,
            WorkCenterId = workCenter?.Id,
        };
        _db.Operations.Add(operation);
        await _db.SaveChangesAsync();
        return operation;
    }

    private async Task AddJobOperationAsync(Job job, Operation operation, JobOperationStatus status)
    {
        _db.JobOperations.Add(new JobOperation
        {
            JobId = job.Id,
            OperationId = operation.Id,
            StepNumber = operation.StepNumber,
            Title = operation.Title,
            Status = status,
        });
        await _db.SaveChangesAsync();
    }

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
