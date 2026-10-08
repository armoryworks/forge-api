using FluentAssertions;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class GetJobsOperationSummaryTests
{
    private readonly JobOperationTestHarness _h = new();
    private readonly Mock<IJobRepository> _repo = new();

    private static JobListResponseModel ListItem(Job job) => new(
        job.Id, job.JobNumber, job.Title, "In Production", "#94a3b8", null, null, null, "Normal",
        null, false, null, null, null, 0, null, null, [], Quantity: 40m);

    private async Task<JobListResponseModel> GetAsync(Job job)
    {
        _repo.Setup(r => r.GetPagedJobsAsync(It.IsAny<JobListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResponse<JobListResponseModel>([ListItem(job)], 1, 1, 200));
        var page = await new GetJobsHandler(_repo.Object, _h.Operations)
            .Handle(new GetJobsQuery(new JobListQuery()), CancellationToken.None);
        return page.Items.Single();
    }

    [Fact]
    public async Task TrackingOff_LeavesTheOperationFieldsNull()
    {
        _h.TrackingEnabled = false;
        var (job, _) = await _h.AddJobWithRoutingAsync(10m);

        var item = await GetAsync(job);

        item.OperationsTotal.Should().BeNull();
        item.OperationsComplete.Should().BeNull();
        item.InProgressSteps.Should().BeNull();
        item.RunningTimerCount.Should().BeNull();
        item.EstimatedRemainingMinutes.Should().BeNull();
        item.Quantity.Should().Be(40m);
    }

    [Fact]
    public async Task TrackingOn_AddsCountsRunningStepsAndTimeLeft()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        _h.Timers.Db.JobOperations.Add(new JobOperation
        {
            JobId = job.Id, OperationId = routing[0].Id, StepNumber = 10, Title = "Saw",
            Status = JobOperationStatus.Complete, CompletedQuantity = 10m,
            EstSetupMinutes = 15m, EstRunMinutesEach = 3m, EstRunMinutesLot = 5m,
        });
        await _h.Timers.Db.SaveChangesAsync();
        await _h.Timers.AddRunningTimerAsync(user.Id, job.Id, _h.Timers.Now.AddMinutes(-5), routing[1].Id);
        await _h.Timers.AddRunningTimerAsync(user.Id, job.Id, _h.Timers.Now.AddMinutes(-2));

        var item = await GetAsync(job);

        item.OperationsTotal.Should().Be(3);
        item.OperationsComplete.Should().Be(1);
        item.InProgressSteps.Should().Equal(20);
        item.RunningTimerCount.Should().Be(2);
        item.EstimatedRemainingMinutes.Should().Be(60m);
    }

    [Fact]
    public async Task CompletedJob_HasNoTimeLeft()
    {
        var (job, _) = await _h.AddJobWithRoutingAsync(10m);
        job.CompletedDate = _h.Timers.Now;
        await _h.Timers.Db.SaveChangesAsync();

        var item = await GetAsync(job);

        item.OperationsTotal.Should().Be(3);
        item.EstimatedRemainingMinutes.Should().Be(0m);
    }

    [Fact]
    public async Task JobWithoutRouting_LeavesTheFieldsNull()
    {
        var job = await _h.Timers.AddJobAsync("JOB-0700");

        var item = await GetAsync(job);

        item.OperationsTotal.Should().BeNull();
        item.EstimatedRemainingMinutes.Should().BeNull();
    }
}
