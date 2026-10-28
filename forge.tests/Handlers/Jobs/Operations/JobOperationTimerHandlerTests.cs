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
    public async Task Start_WhileClockedOut_IsRefusedAndTouchesNothing()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        await _h.Timers.AddClockEventAsync(user.Id, "ClockIn", _h.Timers.Now.AddHours(-3));
        await _h.Timers.AddClockEventAsync(user.Id, "ClockOut", _h.Timers.Now.AddHours(-1));

        var act = () => StartAsync(user.Id, job.Id, routing[0].Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("You're clocked out*");
        (await _h.Timers.Db.TimeEntries.AnyAsync()).Should().BeFalse();
        (await _h.Timers.Db.JobOperations.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Start_AfterAClockOutWeeksAgo_IsRefused()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        await _h.Timers.AddClockEventAsync(user.Id, "ClockIn", _h.Timers.Now.AddDays(-21).AddHours(-8));
        await _h.Timers.AddClockEventAsync(user.Id, "ClockOut", _h.Timers.Now.AddDays(-21));

        var act = () => StartAsync(user.Id, job.Id, routing[0].Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("You're clocked out*");
        (await _h.Timers.Db.TimeEntries.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Start_ForAUserWhoNeverClocks_Starts()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        (await _h.Timers.Db.ClockEvents.AnyAsync(e => e.UserId == user.Id)).Should().BeFalse();

        var result = await StartAsync(user.Id, job.Id, routing[0].Id);

        result.AlreadyRunning.Should().BeFalse();
        (await _h.Timers.Db.TimeEntries.CountAsync(e => e.UserId == user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Start_AfterClockingBackIn_Starts()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        await _h.Timers.AddClockEventAsync(user.Id, "ClockIn", _h.Timers.Now.AddHours(-5));
        await _h.Timers.AddClockEventAsync(user.Id, "ClockOut", _h.Timers.Now.AddHours(-3));
        await _h.Timers.AddClockEventAsync(user.Id, "ClockIn", _h.Timers.Now.AddHours(-1));

        var result = await StartAsync(user.Id, job.Id, routing[0].Id);

        result.AlreadyRunning.Should().BeFalse();
        (await _h.Timers.Db.TimeEntries.CountAsync(e => e.UserId == user.Id)).Should().Be(1);
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
    public async Task Start_RefusedForAClosedStepOrJob_WritesNoTimeEntry()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        _h.Timers.Db.JobOperations.Add(new JobOperation
        {
            JobId = job.Id, OperationId = routing[0].Id, StepNumber = 10, Title = "Saw", Status = JobOperationStatus.Skipped,
        });
        await _h.Timers.Db.SaveChangesAsync();
        var user = await _h.Timers.AddUserAsync();

        var closedStep = () => StartAsync(user.Id, job.Id, routing[0].Id);
        await closedStep.Should().ThrowAsync<InvalidOperationException>().WithMessage(JobOperationRules.ReopenFirstMessage);

        job.IsArchived = true;
        await _h.Timers.Db.SaveChangesAsync();
        var closedJob = () => StartAsync(user.Id, job.Id, routing[1].Id);
        await closedJob.Should().ThrowAsync<InvalidOperationException>().WithMessage("*closed*");

        _h.TrackingEnabled = false;
        var trackingOff = () => StartAsync(user.Id, job.Id, routing[2].Id);
        await trackingOff.Should().ThrowAsync<InvalidOperationException>().WithMessage(JobOperationRules.TrackingOffMessage);

        (await _h.Timers.Db.TimeEntries.AnyAsync()).Should().BeFalse();
        (await _h.Timers.Db.ActivityLogs.AnyAsync(a => a.Action == "timer-started")).Should().BeFalse();
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
    public async Task Stop_WithQuantities_RecordsThemOnTheStepAndStopsTheTimer()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        await StartAsync(user.Id, job.Id, routing[0].Id);
        _h.Timers.Now = _h.Timers.Now.AddMinutes(12);

        var result = await _h.StopHandler(user.Id).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[0].Id, new StopJobOperationTimerRequestModel(GoodQuantity: 4m, ScrapQuantity: 1m, ReasonCode: "chatter")),
            CancellationToken.None);

        result.Stopped.Should().BeTrue();
        result.Entry!.DurationMinutes.Should().Be(12);
        result.Operation!.CompletedQuantity.Should().Be(4m);
        result.Operation.ScrapQuantity.Should().Be(1m);
        var row = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync();
        row.Status.Should().Be(JobOperationStatus.InProgress);
        (await _h.Timers.Db.TimeEntries.AnyAsync(t => t.TimerStop == null)).Should().BeFalse();

        await StartAsync(user.Id, job.Id, routing[0].Id);
        await _h.StopHandler(user.Id).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[0].Id, new StopJobOperationTimerRequestModel(GoodQuantity: 2m)),
            CancellationToken.None);
        (await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync()).CompletedQuantity.Should().Be(6m, "stop quantities add to what is already recorded");
    }

    [Fact]
    public async Task Stop_WithMarkComplete_CompletesTheStep()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        await StartAsync(user.Id, job.Id, routing[1].Id);

        var result = await _h.StopHandler(user.Id).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[1].Id, new StopJobOperationTimerRequestModel(GoodQuantity: 10m, MarkComplete: true)),
            CancellationToken.None);

        result.Stopped.Should().BeTrue();
        result.Operation!.Status.Should().Be(JobOperationStatus.Complete);
        (await _h.Timers.Db.TimeEntries.AnyAsync(t => t.TimerStop == null)).Should().BeFalse();
    }

    [Fact]
    public async Task Stop_PastTheJobQuantity_RecordsTheOverrunAndStopsTheTimer()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        await StartAsync(user.Id, job.Id, routing[0].Id);
        _h.Timers.Now = _h.Timers.Now.AddMinutes(20);

        var result = await _h.StopHandler(user.Id).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[0].Id, new StopJobOperationTimerRequestModel(GoodQuantity: 12m, ScrapQuantity: 1m, MarkComplete: true)),
            CancellationToken.None);

        result.Stopped.Should().BeTrue();
        result.Operation!.CompletedQuantity.Should().Be(12m);
        result.Operation.ScrapQuantity.Should().Be(1m);
        result.Operation.Status.Should().Be(JobOperationStatus.Complete);
        (await _h.Timers.Db.TimeEntries.AnyAsync(t => t.TimerStop == null)).Should().BeFalse();
        var log = await _h.Timers.Db.JobActivityLogs.AsNoTracking().SingleAsync(l => l.Action == ActivityAction.OperationCompleted);
        log.Description.Should().Contain("Overrun: 2 good over the target quantity of 10.");
    }

    [Fact]
    public async Task Stop_PastTheOverrunLimit_IsRefusedAndTheTimerKeepsRunning()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        await StartAsync(user.Id, job.Id, routing[0].Id);
        _h.MaxOverrunPercent = "10";

        var act = () => _h.StopHandler(user.Id).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[0].Id, new StopJobOperationTimerRequestModel(GoodQuantity: 12m, ScrapQuantity: 3m)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        (await _h.Timers.Db.TimeEntries.CountAsync(t => t.TimerStop == null)).Should().Be(1);
        (await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync()).CompletedQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task Stop_WithBlankQuantities_OnlyStopsTheTimer()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        await StartAsync(user.Id, job.Id, routing[0].Id);

        var result = await _h.StopHandler(user.Id).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[0].Id, new StopJobOperationTimerRequestModel()),
            CancellationToken.None);

        result.Stopped.Should().BeTrue();
        result.Operation.Should().BeNull();
        (await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync()).CompletedQuantity.Should().Be(0m);
    }

    [Fact]
    public void Validator_RejectsAnUnknownEntryType()
    {
        var validator = new StartJobOperationTimerValidator();

        validator.Validate(new StartJobOperationTimerCommand(1, 1, new((TimeEntryType)99))).IsValid.Should().BeFalse();
        validator.Validate(new StartJobOperationTimerCommand(1, 1, new(TimeEntryType.Setup))).IsValid.Should().BeTrue();
    }
}
