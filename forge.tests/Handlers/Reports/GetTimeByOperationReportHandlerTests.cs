using FluentAssertions;

using Forge.Api.Features.Reports;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Reports;

public class GetTimeByOperationReportHandlerTests
{
    [Fact]
    public async Task Handle_EstimateSumsEachJobsPlannedMinutes()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "BRK-100", Name = "Bracket" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var op = new Operation
        {
            PartId = part.Id,
            StepNumber = 10,
            Title = "Mold",
            SetupMinutes = 30m,
            EstimatedMs = 30000,
        };
        db.Operations.Add(op);
        var bigJob = new Job { JobNumber = "J-1", Title = "Big", TrackTypeId = 1, CurrentStageId = 1, PartId = part.Id };
        bigJob.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 500m });
        var smallJob = new Job { JobNumber = "J-2", Title = "Small", TrackTypeId = 1, CurrentStageId = 1, PartId = part.Id };
        smallJob.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 100m });
        db.Jobs.AddRange(bigJob, smallJob);
        await db.SaveChangesAsync();

        db.TimeEntries.AddRange(
            new TimeEntry { JobId = bigJob.Id, OperationId = op.Id, UserId = 1, DurationMinutes = 300, EntryType = TimeEntryType.Run },
            new TimeEntry { JobId = smallJob.Id, OperationId = op.Id, UserId = 1, DurationMinutes = 60, EntryType = TimeEntryType.Run });
        await db.SaveChangesAsync();

        var handler = new GetTimeByOperationReportHandler(db);
        var result = await handler.Handle(new GetTimeByOperationReportQuery(null, null, null), CancellationToken.None);

        var row = result.Should().ContainSingle().Subject;
        row.PartNumber.Should().Be("BRK-100");
        row.JobCount.Should().Be(2);
        row.EstimatedHours.Should().Be(6m);
        row.TotalHours.Should().Be(6m);
        row.VariancePercent.Should().Be(0m);
    }
}
