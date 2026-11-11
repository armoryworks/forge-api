using AwesomeAssertions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class SubcontractSendLimitTests
{
    private readonly JobOperationTestHarness _h = new();

    private async Task<(Job Job, Operation Operation, Vendor Vendor)> SeedAsync(decimal jobQuantity = 100m)
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(jobQuantity);
        var vendor = new Vendor { CompanyName = "Plating Co" };
        _h.Timers.Db.Vendors.Add(vendor);
        await _h.Timers.Db.SaveChangesAsync();
        var operation = routing[1];
        operation.IsSubcontract = true;
        operation.SubcontractVendorId = vendor.Id;
        await _h.Timers.Db.SaveChangesAsync();
        return (job, operation, vendor);
    }

    private async Task<SubcontractOrder> AddOrderAsync(
        Job job, Operation operation, Vendor vendor, decimal quantity, SubcontractStatus status,
        decimal? good = null, decimal scrap = 0m, decimal rejected = 0m)
    {
        var order = new SubcontractOrder
        {
            JobId = job.Id, OperationId = operation.Id, VendorId = vendor.Id, Quantity = quantity,
            SentAt = _h.Timers.Now.AddDays(-2), Status = status,
            ReceivedQuantity = good, ScrapQuantity = scrap, RejectedQuantity = rejected,
            ReceivedAt = status is SubcontractStatus.Complete or SubcontractStatus.Rejected ? _h.Timers.Now.AddDays(-1) : null,
        };
        _h.Timers.Db.SubcontractOrders.Add(order);
        await _h.Timers.Db.SaveChangesAsync();
        return order;
    }

    private Task<SubcontractOrderResponseModel> SendAsync(Job job, Operation operation, decimal quantity)
        => new SendOutSubcontractHandler(_h.Timers.Db, Mock.Of<IMediator>(), _h.Timers.Clock.Object, _h.Operations)
            .Handle(new SendOutSubcontractCommand(job.Id, operation.Id, new SendOutRequestModel(quantity, 0m, null, null, null)),
                CancellationToken.None);

    private async Task<SubcontractOperationResponseModel> RoomAsync(Job job)
        => (await new GetJobSubcontractOperationsHandler(_h.Timers.Db, _h.Operations)
            .Handle(new GetJobSubcontractOperationsQuery(job.Id), CancellationToken.None)).Single();

    private static async Task ShouldRefuseAsync(Func<Task> act, decimal max)
    {
        var failure = (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Single();
        failure.ErrorCode.Should().Be(SubcontractSendLimit.Code);
        failure.PropertyName.Should().Be("Quantity");
        failure.FormattedMessagePlaceholderValues["ComparisonValue"].Should().Be(max);
    }

    [Fact]
    public async Task WithNoOverrunLimit_SendOutIsNotBounded()
    {
        var (job, operation, _) = await SeedAsync();

        var sent = await SendAsync(job, operation, 1000m);

        sent.Quantity.Should().Be(1000m);
        var room = await RoomAsync(job);
        room.MaxSendQuantity.Should().BeNull();
        room.RemainingQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task WithAnOverrunLimit_SendOutStopsAtTheLimit()
    {
        var (job, operation, _) = await SeedAsync();
        _h.MaxOverrunPercent = "10";

        await ShouldRefuseAsync(() => SendAsync(job, operation, 1000m), 110m);
        (await _h.Timers.Db.SubcontractOrders.AnyAsync()).Should().BeFalse();

        (await SendAsync(job, operation, 110m)).Quantity.Should().Be(110m);
    }

    [Fact]
    public async Task WhatIsStillOutAndWhatIsDone_CountAgainstTheLimit()
    {
        var (job, operation, vendor) = await SeedAsync();
        _h.MaxOverrunPercent = "0";
        _h.Timers.Db.JobOperations.Add(new JobOperation
        {
            JobId = job.Id, OperationId = operation.Id, StepNumber = operation.StepNumber, Title = operation.Title,
            Status = JobOperationStatus.InProgress, CompletedQuantity = 20m,
        });
        await AddOrderAsync(job, operation, vendor, 60m, SubcontractStatus.PartiallyReceived, good: 20m, scrap: 5m);

        var room = await RoomAsync(job);
        room.JobQuantity.Should().Be(100m);
        room.CommittedQuantity.Should().Be(55m);
        room.RemainingQuantity.Should().Be(45m);
        room.MaxSendQuantity.Should().Be(45m);

        await ShouldRefuseAsync(() => SendAsync(job, operation, 46m), 45m);
        (await SendAsync(job, operation, 45m)).Quantity.Should().Be(45m);
        (await RoomAsync(job)).MaxSendQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task RejectedPieces_CanGoBackOut()
    {
        var (job, operation, vendor) = await SeedAsync();
        _h.MaxOverrunPercent = "0";
        _h.Timers.Db.JobOperations.Add(new JobOperation
        {
            JobId = job.Id, OperationId = operation.Id, StepNumber = operation.StepNumber, Title = operation.Title,
            Status = JobOperationStatus.InProgress, CompletedQuantity = 60m,
        });
        await AddOrderAsync(job, operation, vendor, 100m, SubcontractStatus.Complete, good: 60m, rejected: 40m);

        (await RoomAsync(job)).MaxSendQuantity.Should().Be(40m);
        (await SendAsync(job, operation, 40m)).Quantity.Should().Be(40m);
    }

    [Fact]
    public async Task WithTrackingOff_GoodReceivedBackStandsInForTheStep()
    {
        var (job, operation, vendor) = await SeedAsync();
        _h.MaxOverrunPercent = "0";
        _h.TrackingEnabled = false;
        await AddOrderAsync(job, operation, vendor, 70m, SubcontractStatus.Complete, good: 70m);

        await AddOrderAsync(job, operation, vendor, 10m, SubcontractStatus.Complete, good: 4m, scrap: 6m);

        var room = await RoomAsync(job);
        room.CommittedQuantity.Should().Be(74m);
        room.RemainingQuantity.Should().Be(20m);
        room.MaxSendQuantity.Should().Be(26m);
        await ShouldRefuseAsync(() => SendAsync(job, operation, 27m), 26m);
    }
}
