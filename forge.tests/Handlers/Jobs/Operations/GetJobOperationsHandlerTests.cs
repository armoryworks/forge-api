using FluentAssertions;

using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs.Operations;

public class GetJobOperationsHandlerTests
{
    private readonly JobOperationTestHarness _h = new();
    private readonly GetJobOperationsHandler _handler;

    public GetJobOperationsHandlerTests()
    {
        _handler = new GetJobOperationsHandler(_h.Operations);
    }

    [Fact]
    public async Task Handle_UntouchedJob_ListsTheRoutingAsNotStartedWithQuantityScaledEstimates()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);

        var result = await _handler.Handle(new GetJobOperationsQuery(job.Id), CancellationToken.None);

        result.JobQuantity.Should().Be(10m);
        result.TrackingEnabled.Should().BeTrue();
        result.Operations.Select(o => o.StepNumber).Should().Equal(10, 20, 30);
        result.Operations.Should().OnlyContain(o => o.Status == JobOperationStatus.NotStarted && o.JobOperationId == null);
        result.Operations[0].WorkCenterName.Should().Be("Mill 1");
        result.Operations[0].EstimatedTotalMinutes.Should().Be(50m);
        result.Operations[1].EstimatedRunMinutesEach.Should().Be(2m);
        result.Operations[1].EstimatedTotalMinutes.Should().Be(50m);
        result.Operations[2].EstimatedTotalMinutes.Should().Be(10m);
        result.EstimatedRemainingMinutes.Should().Be(110m);
        result.AllOperationsComplete.Should().BeFalse();
        routing.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_RowsAndTimers_MergeIntoTheRoutingWithLiveActuals()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        var row = new JobOperation
        {
            JobId = job.Id, OperationId = routing[0].Id, StepNumber = 10, Title = "Saw",
            Status = JobOperationStatus.InProgress, CompletedQuantity = 4m, ScrapQuantity = 2m,
            EstSetupMinutes = 15m, EstRunMinutesEach = 3m, EstRunMinutesLot = 5m,
        };
        _h.Timers.Db.JobOperations.Add(row);
        await _h.Timers.Db.SaveChangesAsync();
        _h.Timers.Db.TimeEntries.AddRange(
            new TimeEntry
            {
                UserId = user.Id, JobId = job.Id, OperationId = routing[0].Id, EntryType = TimeEntryType.Setup,
                Date = DateOnly.FromDateTime(_h.Timers.Now.UtcDateTime),
                TimerStart = _h.Timers.Now.AddMinutes(-90), TimerStop = _h.Timers.Now.AddMinutes(-70), DurationMinutes = 20,
            },
            new TimeEntry
            {
                UserId = user.Id, JobId = job.Id, OperationId = routing[0].Id, JobOperationId = row.Id,
                EntryType = TimeEntryType.Run, Date = DateOnly.FromDateTime(_h.Timers.Now.UtcDateTime),
                TimerStart = _h.Timers.Now.AddMinutes(-30),
            });
        await _h.Timers.Db.SaveChangesAsync();

        var result = await _handler.Handle(new GetJobOperationsQuery(job.Id), CancellationToken.None);

        var saw = result.Operations[0];
        saw.JobOperationId.Should().Be(row.Id);
        saw.Version.Should().Be(row.Version);
        saw.Status.Should().Be(JobOperationStatus.InProgress);
        saw.ActualSetupMinutes.Should().Be(20m);
        saw.ActualRunMinutes.Should().Be(30m);
        saw.ActualTotalMinutes.Should().Be(50m);
        saw.ActualRunMinutesEach.Should().Be(7.5m);
        saw.RemainingMinutes.Should().Be(12m);
        saw.OpenTimers.Should().ContainSingle().Which.UserName.Should().Be("Machinist, Pat");
        result.EstimatedRemainingMinutes.Should().Be(72m);
    }

    [Fact]
    public async Task Handle_RowWhoseStepLeftTheRouting_IsListedAfterTheRoutingFromItsSnapshot()
    {
        var (job, _) = await _h.AddJobWithRoutingAsync(10m);
        _h.Timers.Db.JobOperations.Add(new JobOperation
        {
            JobId = job.Id, OperationId = null, StepNumber = 15, Title = "Old heat treat",
            Status = JobOperationStatus.Complete, CompletedQuantity = 10m, EstSetupMinutes = 60m,
        });
        await _h.Timers.Db.SaveChangesAsync();

        var result = await _handler.Handle(new GetJobOperationsQuery(job.Id), CancellationToken.None);

        result.Operations.Should().HaveCount(4);
        var orphan = result.Operations[3];
        orphan.IsRoutingStep.Should().BeFalse();
        orphan.Title.Should().Be("Old heat treat");
        orphan.EstimatedSetupMinutes.Should().Be(60m);
        result.Operations.Take(3).Should().OnlyContain(o => o.IsRoutingStep);
    }

    [Fact]
    public async Task Handle_CompletedRowsOnOtherJobs_GiveAHistoricalPerPieceAverage()
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(10m);
        var user = await _h.Timers.AddUserAsync();
        var earlier = new Job { JobNumber = "JOB-0400", Title = "Earlier", TrackTypeId = 1, CurrentStageId = 1, PartId = job.PartId };
        _h.Timers.Db.Jobs.Add(earlier);
        await _h.Timers.Db.SaveChangesAsync();
        _h.Timers.Db.JobOperations.Add(new JobOperation
        {
            JobId = earlier.Id, OperationId = routing[0].Id, StepNumber = 10, Title = "Saw",
            Status = JobOperationStatus.Complete, CompletedQuantity = 10m, CompletedAt = _h.Timers.Now.AddDays(-2),
        });
        _h.Timers.Db.TimeEntries.Add(new TimeEntry
        {
            UserId = user.Id, JobId = earlier.Id, OperationId = routing[0].Id, EntryType = TimeEntryType.Run,
            Date = DateOnly.FromDateTime(_h.Timers.Now.UtcDateTime),
            TimerStart = _h.Timers.Now.AddDays(-2).AddMinutes(-25), TimerStop = _h.Timers.Now.AddDays(-2), DurationMinutes = 25,
        });
        await _h.Timers.Db.SaveChangesAsync();

        var result = await _handler.Handle(new GetJobOperationsQuery(job.Id), CancellationToken.None);

        result.Operations[0].HistoryRunMinutesEach.Should().Be(2.5m);
        result.Operations[0].HistoryJobCount.Should().Be(1);
        result.Operations[1].HistoryRunMinutesEach.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CompletedJob_HasNoTimeLeft()
    {
        var (job, _) = await _h.AddJobWithRoutingAsync(10m);
        job.CompletedDate = _h.Timers.Now;
        await _h.Timers.Db.SaveChangesAsync();

        var result = await _handler.Handle(new GetJobOperationsQuery(job.Id), CancellationToken.None);

        result.EstimatedRemainingMinutes.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_JobWithoutAPart_ReturnsAnEmptyListAndNoEstimate()
    {
        var job = await _h.Timers.AddJobAsync("JOB-0600");

        var result = await _handler.Handle(new GetJobOperationsQuery(job.Id), CancellationToken.None);

        result.Operations.Should().BeEmpty();
        result.EstimatedRemainingMinutes.Should().BeNull();
        result.JobQuantity.Should().Be(1m);
    }

    [Fact]
    public async Task Handle_UnknownJob_IsNotFound()
    {
        var act = () => _handler.Handle(new GetJobOperationsQuery(9999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_TrackingOff_StillAnswersAndSaysSo()
    {
        _h.TrackingEnabled = false;
        var (job, _) = await _h.AddJobWithRoutingAsync(10m);

        var result = await _handler.Handle(new GetJobOperationsQuery(job.Id), CancellationToken.None);

        result.TrackingEnabled.Should().BeFalse();
        result.Operations.Should().HaveCount(3);
    }
}
