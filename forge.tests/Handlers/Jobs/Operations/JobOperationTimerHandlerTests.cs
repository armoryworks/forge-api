using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs.Operations;

public class JobOperationTimerHandlerTests
{
    private readonly JobOperationTestHarness _h = new();

    private Task<JobOperationTimerResponseModel> StartAsync(int userId, int jobId, int operationId)
        => _h.StartHandler(userId).Handle(
            new StartJobOperationTimerCommand(jobId, operationId, new StartJobOperationTimerRequestModel()),
            CancellationToken.None);

    [Fact]
    public async Task Start_FirstTouch_CreatesTheRowMarksItInProgressAndStartsATimer()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();

        var result = await StartAsync(user.Id, job.Id, routing[0].Id);

        result.AlreadyRunning.Should().BeFalse();
        result.Operation.Status.Should().Be(JobOperationStatus.InProgress);
        var row = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync();
        row.StepNumber.Should().Be(10);
        row.EstSetupMinutes.Should().Be(15m);
        row.EstRunMinutesEach.Should().Be(3m);
        row.StartedAt.Should().Be(_h.Timers.Now);
        var entry = await _h.Timers.Db.TimeEntries.AsNoTracking().SingleAsync();
        entry.Id.Should().Be(result.Entry.Id);
        entry.JobOperationId.Should().Be(row.Id);
        entry.OperationId.Should().Be(routing[0].Id);
        entry.EntryType.Should().Be(TimeEntryType.Run);
        entry.TimerStart.Should().Be(_h.Timers.Now);
        var log = await _h.Timers.Db.JobActivityLogs.AsNoTracking().SingleAsync();
        log.Action.Should().Be(ActivityAction.OperationStarted);
        log.OperationId.Should().Be(routing[0].Id);
        (await _h.Timers.Db.ActivityLogs.AnyAsync(a => a.Action == "timer-started" && a.EntityId == entry.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Start_Twice_ReturnsTheRunningTimerInsteadOfAConflict()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();

        var first = await StartAsync(user.Id, job.Id, routing[0].Id);
        var second = await StartAsync(user.Id, job.Id, routing[0].Id);

        second.AlreadyRunning.Should().BeTrue();
        second.Entry.Id.Should().Be(first.Entry.Id);
        (await _h.Timers.Db.TimeEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Start_TwoStepsForOneUser_RunAtTheSameTime()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();

        await StartAsync(user.Id, job.Id, routing[1].Id);
        await StartAsync(user.Id, job.Id, routing[2].Id);

        (await _h.Timers.Db.TimeEntries.CountAsync(t => t.TimerStop == null)).Should().Be(2);
        (await _h.Timers.Db.JobOperations.CountAsync(r => r.Status == JobOperationStatus.InProgress)).Should().Be(2);
    }

    [Fact]
    public async Task Start_OperationNotOnTheJobsRouting_IsNotFound()
    {
        var (job, _) = await _h.AddJobWithRoutingAsync(10m);
        var (_, otherRouting) = await _h.AddJobWithRoutingAsync(5m, "JOB-0501");
        var user = await _h.Timers.AddUserAsync();

        var act = () => StartAsync(user.Id, job.Id, otherRouting[0].Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Start_CompletedJob_IsRefused()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        job.CompletedDate = _h.Timers.Now.AddDays(-1);
        await _h.Timers.Db.SaveChangesAsync();
        var user = await _h.Timers.AddUserAsync();

        var act = () => StartAsync(user.Id, job.Id, routing[0].Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already complete*");
    }

    [Fact]
    public async Task Start_CompletedOperation_AsksToReopenFirst()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        _h.Timers.Db.JobOperations.Add(new JobOperation
        {
            JobId = job.Id, OperationId = routing[0].Id, StepNumber = 10, Title = "Saw", Status = JobOperationStatus.Complete,
        });
        await _h.Timers.Db.SaveChangesAsync();
        var user = await _h.Timers.AddUserAsync();

        var act = () => StartAsync(user.Id, job.Id, routing[0].Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(JobOperationRules.ReopenFirstMessage);
    }

    [Fact]
    public async Task Start_TrackingOff_IsRefusedButStopStillWorks()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        await StartAsync(user.Id, job.Id, routing[0].Id);
        _h.TrackingEnabled = false;
        _h.Timers.Now = _h.Timers.Now.AddMinutes(12);

        var start = () => StartAsync(user.Id, job.Id, routing[1].Id);
        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage(JobOperationRules.TrackingOffMessage);

        var stopped = await _h.StopHandler(user.Id).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[0].Id, new StopJobOperationTimerRequestModel()),
            CancellationToken.None);

        stopped.Stopped.Should().BeTrue();
        stopped.Entry!.DurationMinutes.Should().Be(12);
        (await _h.Timers.Db.TimeEntries.AnyAsync(t => t.TimerStop == null)).Should().BeFalse();
    }

    [Fact]
    public async Task Stop_NothingRunning_ReportsNotStopped()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();

        var result = await _h.StopHandler(user.Id).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[0].Id, new StopJobOperationTimerRequestModel()),
            CancellationToken.None);

        result.Should().Be(new JobOperationTimerStopResponseModel(false, null));
    }

    [Fact]
    public async Task Stop_LeavesTheUsersOtherTimersRunning()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        var general = await _h.Timers.AddRunningTimerAsync(user.Id, job.Id, _h.Timers.Now.AddMinutes(-60));
        await StartAsync(user.Id, job.Id, routing[0].Id);
        var other = await StartAsync(user.Id, job.Id, routing[1].Id);

        await _h.StopHandler(user.Id).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[0].Id, new StopJobOperationTimerRequestModel()),
            CancellationToken.None);

        var open = await _h.Timers.Db.TimeEntries.AsNoTracking().Where(t => t.TimerStop == null).Select(t => t.Id).ToListAsync();
        open.Should().BeEquivalentTo([general.Id, other.Entry.Id]);
    }

    [Fact]
    public void Validator_RejectsAnUnknownEntryType()
    {
        var validator = new StartJobOperationTimerValidator();

        validator.Validate(new StartJobOperationTimerCommand(1, 1, new((TimeEntryType)99))).IsValid.Should().BeFalse();
        validator.Validate(new StartJobOperationTimerCommand(1, 1, new(TimeEntryType.Setup))).IsValid.Should().BeTrue();
    }
}
