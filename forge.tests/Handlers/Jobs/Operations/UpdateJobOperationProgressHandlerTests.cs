using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs.Operations;

public class UpdateJobOperationProgressHandlerTests
{
    private readonly JobOperationTestHarness _h = new();

    private Task<JobOperationProgressResponseModel> PatchAsync(
        int userId, int jobId, int operationId, UpdateJobOperationProgressRequestModel data)
        => _h.ProgressHandler(userId).Handle(
            new UpdateJobOperationProgressCommand(jobId, operationId, data), CancellationToken.None);

    private Task StartAsync(int userId, int jobId, int operationId)
        => _h.StartHandler(userId).Handle(
            new StartJobOperationTimerCommand(jobId, operationId, new StartJobOperationTimerRequestModel()),
            CancellationToken.None);

    [Fact]
    public async Task Quantities_OnAnUntouchedStep_MoveItToInProgress()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();

        var result = await PatchAsync(user.Id, job.Id, routing[1].Id, new(20m, null, null));

        result.Operation.Status.Should().Be(JobOperationStatus.InProgress);
        result.Operation.CompletedQuantity.Should().Be(20m);
        result.AllOperationsComplete.Should().BeFalse();
        var log = await _h.Timers.Db.JobActivityLogs.AsNoTracking().SingleAsync();
        log.Action.Should().Be(ActivityAction.OperationProgress);
        log.OperationId.Should().Be(routing[1].Id);
        log.OldValue.Should().Be("NotStarted 0/40");
        log.NewValue.Should().Be("InProgress 20/40");
    }

    [Fact]
    public async Task Complete_WithoutQuantity_FillsTheRestAndStopsEveryTimerOnThatStepOnly()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var first = await _h.Timers.AddUserAsync();
        var second = await _h.Timers.AddUserAsync();
        await StartAsync(first.Id, job.Id, routing[1].Id);
        await StartAsync(second.Id, job.Id, routing[1].Id);
        await StartAsync(first.Id, job.Id, routing[2].Id);
        _h.Timers.Now = _h.Timers.Now.AddMinutes(30);

        var result = await PatchAsync(first.Id, job.Id, routing[1].Id, new(null, 1m, JobOperationStatus.Complete));

        result.Operation.Status.Should().Be(JobOperationStatus.Complete);
        result.Operation.CompletedQuantity.Should().Be(39m);
        result.Operation.ScrapQuantity.Should().Be(1m);
        result.Operation.CompletedByName.Should().Be("Machinist, Pat");
        result.Operation.RemainingMinutes.Should().Be(0m);
        var timers = await _h.Timers.Db.TimeEntries.AsNoTracking().ToListAsync();
        timers.Where(t => t.OperationId == routing[1].Id).Should()
            .HaveCount(2).And.OnlyContain(t => t.TimerStop == _h.Timers.Now && t.DurationMinutes == 30);
        timers.Where(t => t.OperationId == routing[1].Id).Should()
            .OnlyContain(t => t.Notes!.Contains("operation completed by Machinist, Pat"));
        timers.Single(t => t.OperationId == routing[2].Id).TimerStop.Should().BeNull();
        var log = await _h.Timers.Db.JobActivityLogs.AsNoTracking()
            .SingleAsync(l => l.Action == ActivityAction.OperationCompleted);
        log.NewValue.Should().Be("Complete 39/40 (1 scrap)");
    }

    [Fact]
    public async Task StaleExpectedVersion_IsAConflict()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();
        var first = await PatchAsync(user.Id, job.Id, routing[0].Id, new(5m, null, null));
        await PatchAsync(user.Id, job.Id, routing[0].Id, new(10m, null, null, first.Operation.Version));

        var act = () => PatchAsync(user.Id, job.Id, routing[0].Id, new(15m, null, null, first.Operation.Version));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(JobOperationRules.StaleMessage);
        (await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync()).CompletedQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task MoreThanTheJobQuantity_IsRejected()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();

        var act = () => PatchAsync(user.Id, job.Id, routing[0].Id, new(39m, 2m, null));

        (await act.Should().ThrowAsync<ValidationException>()).WithMessage("*39*2*40*");
    }

    [Fact]
    public async Task Reopen_ClearsTheCompletion()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();
        await PatchAsync(user.Id, job.Id, routing[0].Id, new(null, null, JobOperationStatus.Complete));

        var result = await PatchAsync(user.Id, job.Id, routing[0].Id, new(null, null, JobOperationStatus.InProgress));

        result.Operation.Status.Should().Be(JobOperationStatus.InProgress);
        result.Operation.CompletedAt.Should().BeNull();
        result.Operation.CompletedByName.Should().BeNull();
        result.Operation.CompletedQuantity.Should().Be(40m);
        (await _h.Timers.Db.JobActivityLogs.CountAsync(l => l.Action == ActivityAction.OperationReopened)).Should().Be(1);
    }

    [Fact]
    public async Task Reset_IsRefusedWhileATimerRunsAndClearsEverythingOtherwise()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();
        await StartAsync(user.Id, job.Id, routing[0].Id);
        await PatchAsync(user.Id, job.Id, routing[0].Id, new(12m, 1m, null));

        var refused = () => PatchAsync(user.Id, job.Id, routing[0].Id, new(null, null, JobOperationStatus.NotStarted));
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Stop the running timers*");

        var open = await _h.Timers.Db.TimeEntries.SingleAsync(t => t.TimerStop == null);
        open.TimerStop = _h.Timers.Now;
        await _h.Timers.Db.SaveChangesAsync();
        var result = await PatchAsync(user.Id, job.Id, routing[0].Id, new(null, null, JobOperationStatus.NotStarted));

        result.Operation.Status.Should().Be(JobOperationStatus.NotStarted);
        result.Operation.CompletedQuantity.Should().Be(0m);
        result.Operation.ScrapQuantity.Should().Be(0m);
        result.Operation.StartedAt.Should().BeNull();
    }

    [Fact]
    public async Task LastStepClosed_ReportsAllOperationsCompleteWithoutMovingTheJob()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();
        var stageBefore = job.CurrentStageId;

        await PatchAsync(user.Id, job.Id, routing[0].Id, new(null, null, JobOperationStatus.Complete));
        await PatchAsync(user.Id, job.Id, routing[1].Id, new(null, null, JobOperationStatus.Skipped));
        var result = await PatchAsync(user.Id, job.Id, routing[2].Id, new(null, null, JobOperationStatus.Complete));

        result.AllOperationsComplete.Should().BeTrue();
        result.EstimatedRemainingMinutes.Should().Be(0m);
        var saved = await _h.Timers.Db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        saved.CurrentStageId.Should().Be(stageBefore);
        saved.CompletedDate.Should().BeNull();
    }

    [Fact]
    public async Task TrackingOff_IsRefused()
    {
        _h.TrackingEnabled = false;
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();

        var act = () => PatchAsync(user.Id, job.Id, routing[0].Id, new(1m, null, null));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(JobOperationRules.TrackingOffMessage);
        (await _h.Timers.Db.JobOperations.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public void Validator_RequiresSomethingToChangeAndNonNegativeQuantities()
    {
        var validator = new UpdateJobOperationProgressValidator();

        validator.Validate(new UpdateJobOperationProgressCommand(1, 1, new(null, null, null))).IsValid.Should().BeFalse();
        validator.Validate(new UpdateJobOperationProgressCommand(1, 1, new(-1m, null, null))).IsValid.Should().BeFalse();
        validator.Validate(new UpdateJobOperationProgressCommand(1, 1, new(null, -1m, null))).IsValid.Should().BeFalse();
        validator.Validate(new UpdateJobOperationProgressCommand(1, 1, new(3m, 0m, null))).IsValid.Should().BeTrue();
        validator.Validate(new UpdateJobOperationProgressCommand(1, 1, new(null, null, null, ReworkQuantity: 2m))).IsValid.Should().BeTrue();
        validator.Validate(new UpdateJobOperationProgressCommand(1, 1, new(null, null, null, ReworkQuantity: -1m))).IsValid.Should().BeFalse();
        validator.Validate(new UpdateJobOperationProgressCommand(1, 1, new(1m, null, null, ReasonCode: new string('X', 51)))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Quantities_AppendAGoodAndAScrapEventForTheChangeOnly()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();
        await PatchAsync(user.Id, job.Id, routing[0].Id, new(10m, null, null));

        await PatchAsync(user.Id, job.Id, routing[0].Id, new(15m, 2m, null, ReasonCode: " BURR "));

        var events = await _h.Timers.Db.JobOperationEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        events.Select(e => (e.Kind, e.Quantity, e.ReasonCode)).Should().Equal(
            (JobOperationEventKind.Good, 10m, (string?)null),
            (JobOperationEventKind.Good, 5m, "BURR"),
            (JobOperationEventKind.Scrap, 2m, "BURR"));
        events.Should().OnlyContain(e => e.UserId == user.Id && e.OccurredAt == _h.Timers.Now);
    }

    [Fact]
    public async Task Rework_IsRecordedAsAnEventWithoutTouchingTheGoodOrScrapCounts()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();

        var result = await PatchAsync(user.Id, job.Id, routing[0].Id, new(null, null, null, ReworkQuantity: 3m, ReasonCode: "OVERSIZE"));

        result.Operation.CompletedQuantity.Should().Be(0m);
        result.Operation.ScrapQuantity.Should().Be(0m);
        var evt = await _h.Timers.Db.JobOperationEvents.AsNoTracking().SingleAsync();
        evt.Kind.Should().Be(JobOperationEventKind.Rework);
        evt.Quantity.Should().Be(3m);
        evt.ReasonCode.Should().Be("OVERSIZE");
        var log = await _h.Timers.Db.JobActivityLogs.AsNoTracking().SingleAsync();
        log.Description.Should().EndWith("3 sent to rework. Reason: OVERSIZE.");
    }

    [Fact]
    public async Task CompleteAndReset_RecordTheFilledAndTheReversedQuantities()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();

        await PatchAsync(user.Id, job.Id, routing[0].Id, new(null, 1m, JobOperationStatus.Complete));
        await PatchAsync(user.Id, job.Id, routing[0].Id, new(null, null, JobOperationStatus.NotStarted));

        var events = await _h.Timers.Db.JobOperationEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        events.Select(e => (e.Kind, e.Quantity)).Should().Equal(
            (JobOperationEventKind.Good, 39m),
            (JobOperationEventKind.Scrap, 1m),
            (JobOperationEventKind.Good, -39m),
            (JobOperationEventKind.Scrap, -1m));
    }

    [Fact]
    public async Task StatusOnlyChange_AppendsNoEvent()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();

        await PatchAsync(user.Id, job.Id, routing[0].Id, new(null, null, JobOperationStatus.Skipped));

        (await _h.Timers.Db.JobOperationEvents.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RejectedQuantity_AppendsNoEvent()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();

        var act = () => PatchAsync(user.Id, job.Id, routing[0].Id, new(39m, 2m, null, ReworkQuantity: 1m));

        await act.Should().ThrowAsync<ValidationException>();
        (await _h.Timers.Db.JobOperationEvents.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CompletingAStep_BroadcastsTheJobToTheBoard()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(40m);
        var user = await _h.Timers.AddUserAsync();

        await PatchAsync(user.Id, job.Id, routing[0].Id, new(null, null, JobOperationStatus.Complete));

        _h.BoardGroup.Verify(g => g.SendCoreAsync(
            "jobUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
