using FluentAssertions;

using Forge.Api.Features.Scheduling;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scheduling;

public class ScheduleOperationHandlerTests
{
    [Fact]
    public async Task LockOperation_SetsIsLocked()
    {
        using var db = TestDbContextFactory.Create();
        var wc = new WorkCenter { Name = "WC", Code = "WC-1", DailyCapacityHours = 8, EfficiencyPercent = 100, NumberOfMachines = 1 };
        db.WorkCenters.Add(wc);
        var part = new Part { PartNumber = "P-001", Description = "Test", Status = PartStatus.Active };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var op = new Operation { PartId = part.Id, StepNumber = 1, Title = "Mill" };
        db.Operations.Add(op);
        var job = new Job { JobNumber = "J-001", Title = "Test Job", PartId = part.Id };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var schedOp = new ScheduledOperation
        {
            JobId = job.Id,
            OperationId = op.Id,
            WorkCenterId = wc.Id,
            ScheduledStart = DateTimeOffset.UtcNow,
            ScheduledEnd = DateTimeOffset.UtcNow.AddHours(2),
            SetupHours = 0.5m,
            RunHours = 1.5m,
            TotalHours = 2m,
            Status = ScheduledOperationStatus.Scheduled,
            SequenceNumber = 1,
        };
        db.ScheduledOperations.Add(schedOp);
        await db.SaveChangesAsync();

        var handler = new LockScheduledOperationHandler(db);
        await handler.Handle(new LockScheduledOperationCommand(schedOp.Id, true), CancellationToken.None);

        var updated = await db.ScheduledOperations.FindAsync(schedOp.Id);
        updated!.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task GetScheduleRuns_ReturnsEmpty()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new GetScheduleRunsHandler(db);

        var result = await handler.Handle(new GetScheduleRunsQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetScheduleRuns_ReturnsSorted()
    {
        using var db = TestDbContextFactory.Create();
        db.ScheduleRuns.AddRange(
            new ScheduleRun { RunDate = DateTimeOffset.UtcNow.AddDays(-2), Direction = ScheduleDirection.Forward, Status = ScheduleRunStatus.Completed },
            new ScheduleRun { RunDate = DateTimeOffset.UtcNow, Direction = ScheduleDirection.Forward, Status = ScheduleRunStatus.Completed }
        );
        await db.SaveChangesAsync();

        var handler = new GetScheduleRunsHandler(db);
        var result = await handler.Handle(new GetScheduleRunsQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].RunDate.Should().BeAfter(result[1].RunDate);
    }
}
