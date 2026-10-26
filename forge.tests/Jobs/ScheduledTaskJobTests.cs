using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.ScheduledTasks;
using Forge.Api.Features.Terminology;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class ScheduledTaskJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 5, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IMediator> _mediator = new();
    private int _jobNumber;

    public ScheduledTaskJobTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(() => Now);
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"J-{++_jobNumber}");
        var handler = new RunScheduledTaskHandler(
            _db, jobRepo.Object, new Mock<IBusinessIdentifierService>().Object, _clock.Object, new TerminologyLabels(_db));
        _mediator
            .Setup(m => m.Send(It.IsAny<RunScheduledTaskCommand>(), It.IsAny<CancellationToken>()))
            .Returns((RunScheduledTaskCommand cmd, CancellationToken ct) => handler.Handle(cmd, ct));
    }

    private ScheduledTaskJob Job() => new(_db, _mediator.Object, _clock.Object, NullLogger<ScheduledTaskJob>.Instance);

    private async Task<ScheduledTask> AddTaskAsync(DateTimeOffset nextRunAt)
    {
        var trackType = new TrackType { Name = "Maintenance", Code = "MAINT" };
        _db.TrackTypes.Add(trackType);
        await _db.SaveChangesAsync();
        _db.JobStages.Add(new JobStage { Name = "Queued", TrackTypeId = trackType.Id, SortOrder = 1 });
        var task = new ScheduledTask { Name = "Gage review", TrackTypeId = trackType.Id, CronExpression = "0 8 * * 1", NextRunAt = nextRunAt };
        _db.ScheduledTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    [Fact]
    public async Task A_due_task_creates_one_job_and_a_second_pass_creates_none()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-5));

        await Job().RunDueTasksAsync();
        await Job().RunDueTasksAsync();

        (await _db.Jobs.CountAsync(j => j.Title == "Gage review")).Should().Be(1);
        (await _db.ScheduledTasks.SingleAsync(t => t.Id == task.Id)).NextRunAt
            .Should().Be(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_task_weeks_overdue_runs_once_and_skips_the_missed_occurrences()
    {
        var task = await AddTaskAsync(Now.AddDays(-21));

        await Job().RunDueTasksAsync();
        await Job().RunDueTasksAsync();

        (await _db.Jobs.CountAsync(j => j.Title == "Gage review")).Should().Be(1);
        (await _db.ScheduledTasks.SingleAsync(t => t.Id == task.Id)).NextRunAt.Should().BeAfter(Now);
    }

    [Fact]
    public async Task A_task_not_yet_due_creates_nothing()
    {
        await AddTaskAsync(Now.AddDays(7));

        await Job().RunDueTasksAsync();

        (await _db.Jobs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_manual_run_leaves_the_schedule_unchanged()
    {
        var next = Now.AddDays(7);
        var task = await AddTaskAsync(next);

        await _mediator.Object.Send(new RunScheduledTaskCommand(task.Id));

        (await _db.Jobs.CountAsync()).Should().Be(1);
        (await _db.ScheduledTasks.SingleAsync(t => t.Id == task.Id)).NextRunAt.Should().Be(next);
    }
}
