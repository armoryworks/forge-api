using FluentAssertions;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class GetJobOperationTimeSummaryHandlerTests
{
    [Fact]
    public async Task Handle_RoutingWithOnlyEstimatedMs_EstimatesFromJobQuantity()
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
            SetupMinutes = 15m,
            EstimatedMs = 30000,
        };
        db.Operations.Add(op);
        var job = new Job
        {
            JobNumber = "J-1",
            Title = "Job",
            TrackTypeId = 1,
            CurrentStageId = 1,
            PartId = part.Id,
        };
        job.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 500m });
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        db.TimeEntries.Add(new TimeEntry
        {
            JobId = job.Id,
            OperationId = op.Id,
            UserId = 1,
            DurationMinutes = 300,
            EntryType = TimeEntryType.Run,
        });
        await db.SaveChangesAsync();

        var handler = new GetJobOperationTimeSummaryHandler(db);
        var result = await handler.Handle(new GetJobOperationTimeSummaryQuery(job.Id), CancellationToken.None);

        var row = result.Should().ContainSingle().Subject;
        row.EstimatedSetupMinutes.Should().Be(15m);
        row.EstimatedRunMinutes.Should().Be(250m);
        row.RunVarianceMinutes.Should().Be(50m);
    }
}
