using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class ReceiveBackSubcontractOperationTests
{
    private readonly JobOperationTestHarness _h = new();

    private ReceiveBackSubcontractHandler Handler()
        => new(_h.Timers.Db, _h.Operations, _h.Timers.TimerStopService(), _h.BoardHub.Object,
            _h.Timers.Mediator.Object, _h.Timers.Clock.Object);

    private async Task<(Job Job, Operation Operation, SubcontractOrder Order)> SeedAsync(bool withOperationRow)
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var operation = routing[1];
        var vendor = new Vendor { CompanyName = "Plating Co" };
        _h.Timers.Db.Vendors.Add(vendor);
        await _h.Timers.Db.SaveChangesAsync();

        if (withOperationRow)
        {
            _h.Timers.Db.JobOperations.Add(new JobOperation
            {
                JobId = job.Id, OperationId = operation.Id, StepNumber = operation.StepNumber, Title = operation.Title,
            });
        }

        var order = new SubcontractOrder
        {
            JobId = job.Id,
            OperationId = operation.Id,
            VendorId = vendor.Id,
            Quantity = 10m,
            SentAt = _h.Timers.Now.AddDays(-3),
            Status = SubcontractStatus.Sent,
        };
        _h.Timers.Db.SubcontractOrders.Add(order);
        await _h.Timers.Db.SaveChangesAsync();
        return (job, operation, order);
    }

    private Task<SubcontractOrderResponseModel> ReceiveAsync(
        int orderId, decimal good, decimal scrap = 0m, bool passed = true)
        => Handler().Handle(
            new ReceiveBackSubcontractCommand(orderId,
                new ReceiveBackRequestModel(good, null, PassedInspection: passed, ScrapQuantity: scrap)),
            CancellationToken.None);

    [Fact]
    public async Task PartialReceiveBack_AddsTheQuantitiesAndLeavesTheStepInProgress()
    {
        var (_, operation, order) = await SeedAsync(withOperationRow: true);

        await ReceiveAsync(order.Id, good: 6m, scrap: 1m);

        var row = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync();
        row.CompletedQuantity.Should().Be(6m);
        row.ScrapQuantity.Should().Be(1m);
        row.Status.Should().Be(JobOperationStatus.InProgress);
        row.StartedAt.Should().Be(_h.Timers.Now);
        row.CompletedAt.Should().BeNull();
        var log = await _h.Timers.Db.JobActivityLogs.AsNoTracking()
            .SingleAsync(a => a.FieldName == "OperationStatus");
        log.Action.Should().Be(ActivityAction.OperationProgress);
        log.OperationId.Should().Be(operation.Id);
        log.OldValue.Should().Be("NotStarted 0/10");
        log.NewValue.Should().Be("InProgress 6/10 (1 scrap)");
        log.CreatedAt.Should().Be(_h.Timers.Now);
    }

    [Fact]
    public async Task FullReceiveBack_CompletesTheStep()
    {
        var (_, _, order) = await SeedAsync(withOperationRow: true);

        await ReceiveAsync(order.Id, good: 9m, scrap: 1m);

        var row = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync();
        row.CompletedQuantity.Should().Be(9m);
        row.ScrapQuantity.Should().Be(1m);
        row.Status.Should().Be(JobOperationStatus.Complete);
        row.CompletedAt.Should().Be(_h.Timers.Now);
        (await _h.Timers.Db.JobActivityLogs.AsNoTracking().SingleAsync(a => a.FieldName == "OperationStatus"))
            .Action.Should().Be(ActivityAction.OperationCompleted);
    }

    [Fact]
    public async Task ReceiveBack_OnTopOfEarlierProgress_CompletesWhenTheJobQuantityIsReached()
    {
        var (_, _, order) = await SeedAsync(withOperationRow: true);
        var row = await _h.Timers.Db.JobOperations.SingleAsync();
        row.Status = JobOperationStatus.InProgress;
        row.CompletedQuantity = 4m;
        await _h.Timers.Db.SaveChangesAsync();

        await ReceiveAsync(order.Id, good: 6m);

        var after = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync();
        after.CompletedQuantity.Should().Be(10m);
        after.Status.Should().Be(JobOperationStatus.Complete);
    }

    [Fact]
    public async Task ReceiveBack_JobWithNoOperationRows_IsUnaffected()
    {
        var (_, _, order) = await SeedAsync(withOperationRow: false);

        var result = await ReceiveAsync(order.Id, good: 10m);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        (await _h.Timers.Db.JobOperations.AnyAsync()).Should().BeFalse();
        (await _h.Timers.Db.JobActivityLogs.AnyAsync(a => a.FieldName == "OperationStatus")).Should().BeFalse();
    }

    [Fact]
    public async Task FailedInspection_DoesNotAdvanceTheStep()
    {
        var (_, _, order) = await SeedAsync(withOperationRow: true);

        var result = await ReceiveAsync(order.Id, good: 10m, passed: false);

        result.Status.Should().Be(nameof(SubcontractStatus.Rejected));
        var row = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync();
        row.CompletedQuantity.Should().Be(0m);
        row.Status.Should().Be(JobOperationStatus.NotStarted);
    }

    [Fact]
    public async Task TrackingOff_LeavesAnExistingRowAloneAndStillReceivesTheOrder()
    {
        var (_, _, order) = await SeedAsync(withOperationRow: true);
        _h.TrackingEnabled = false;

        var result = await ReceiveAsync(order.Id, good: 10m);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        var row = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync();
        row.CompletedQuantity.Should().Be(0m);
        row.Status.Should().Be(JobOperationStatus.NotStarted);
        row.StartedAt.Should().BeNull();
        (await _h.Timers.Db.JobActivityLogs.AnyAsync(a => a.FieldName == "OperationStatus")).Should().BeFalse();
        _h.BoardGroup.Verify(g => g.SendCoreAsync(
            "jobUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FullLotOnTopOfEarlierProgress_StopsAtTheJobQuantityAndStaysEditable()
    {
        var (job, operation, order) = await SeedAsync(withOperationRow: true);
        var row = await _h.Timers.Db.JobOperations.SingleAsync();
        row.Status = JobOperationStatus.InProgress;
        row.CompletedQuantity = 4m;
        await _h.Timers.Db.SaveChangesAsync();

        await ReceiveAsync(order.Id, good: 9m, scrap: 1m);

        var after = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync();
        after.CompletedQuantity.Should().Be(9m);
        after.ScrapQuantity.Should().Be(1m);
        after.Status.Should().Be(JobOperationStatus.Complete);
        var log = await _h.Timers.Db.JobActivityLogs.AsNoTracking()
            .SingleAsync(a => a.FieldName == "OperationStatus");
        log.NewValue.Should().Be("Complete 9/10 (1 scrap)");
        log.Description.Should().Contain("4 over the job quantity not counted");

        var user = await _h.Timers.AddUserAsync();
        _h.Timers.Db.ChangeTracker.Clear();
        var edited = await _h.ProgressHandler(user.Id).Handle(
            new UpdateJobOperationProgressCommand(job.Id, operation.Id,
                new(8m, null, JobOperationStatus.InProgress)),
            CancellationToken.None);

        edited.Operation.Status.Should().Be(JobOperationStatus.InProgress);
        edited.Operation.CompletedQuantity.Should().Be(8m);
    }

    [Fact]
    public async Task FullReceiveBack_StopsTimersOnTheStepAndBroadcastsTheJob()
    {
        var (_, _, order) = await SeedAsync(withOperationRow: true);
        var user = await _h.Timers.AddUserAsync();
        var row = await _h.Timers.Db.JobOperations.SingleAsync();
        await _h.Timers.AddRunningOperationTimerAsync(user.Id, row, _h.Timers.Now.AddMinutes(-30));

        await ReceiveAsync(order.Id, good: 10m);

        var timer = await _h.Timers.Db.TimeEntries.AsNoTracking().SingleAsync();
        timer.TimerStop.Should().Be(_h.Timers.Now);
        timer.DurationMinutes.Should().Be(30);
        timer.Notes.Should().Contain("stopped: operation completed");
        _h.BoardGroup.Verify(g => g.SendCoreAsync(
            "jobUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("archived")]
    [InlineData("disposition")]
    public async Task ClosedJob_DoesNotAdvanceTheStep(string closedBy)
    {
        var (job, _, order) = await SeedAsync(withOperationRow: true);
        switch (closedBy)
        {
            case "completed": job.CompletedDate = _h.Timers.Now.AddDays(-1); break;
            case "archived": job.IsArchived = true; break;
            default: job.Disposition = JobDisposition.AddToInventory; break;
        }
        await _h.Timers.Db.SaveChangesAsync();

        var result = await ReceiveAsync(order.Id, good: 10m);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        var row = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync();
        row.CompletedQuantity.Should().Be(0m);
        row.Status.Should().Be(JobOperationStatus.NotStarted);
        (await _h.Timers.Db.JobActivityLogs.AnyAsync(a => a.FieldName == "OperationStatus")).Should().BeFalse();
    }
}
