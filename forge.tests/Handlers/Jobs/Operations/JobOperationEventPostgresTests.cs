using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs.Operations;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs.Operations;

[Collection(PostgresCollection.Name)]
public sealed class JobOperationEventPostgresTests(PostgresFixture fixture)
{
    private const int UserId = 434343;

    private static JobOperationService Service(AppDbContext db)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        return new JobOperationService(db, Mock.Of<ISettingsService>(), clock.Object);
    }

    private async Task<(int JobId, int OperationId)> SeedAsync()
    {
        await using var seed = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var track = new TrackType { Name = $"EvtTrack {suffix}", Code = $"evttrack-{suffix}", IsActive = true };
        seed.TrackTypes.Add(track);
        await seed.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Machining", Code = "machining", SortOrder = 1, IsActive = true };
        var part = new Part { PartNumber = $"EVT-{suffix}", Name = "Bracket" };
        seed.AddRange(stage, part);
        await seed.SaveChangesAsync();
        var operation = new Operation { PartId = part.Id, StepNumber = 10, Title = "Mill", RunMinutesEach = 2m };
        var job = new Job
        {
            JobNumber = $"J-EVT-{suffix}",
            Title = "Operation events",
            TrackTypeId = track.Id,
            CurrentStageId = stage.Id,
            PartId = part.Id,
        };
        seed.Operations.Add(operation);
        seed.Jobs.Add(job);
        await seed.SaveChangesAsync();
        return (job.Id, operation.Id);
    }

    [Fact]
    public async Task Good_scrap_and_rework_events_round_trip_through_the_declared_schema()
    {
        var (jobId, operationId) = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var service = Service(db);
            var (job, operation) = await service.FindRoutingStepAsync(jobId, operationId, CancellationToken.None);
            var row = await service.EnsureRowAsync(job, operation, CancellationToken.None);
            service.ApplyProgress(row, 4m, 1m, 2m, "TOOL", UserId, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        await using var verify = fixture.CreateContext();
        var events = await new GetJobOperationEventsHandler(verify)
            .Handle(new GetJobOperationEventsQuery(jobId, operationId), CancellationToken.None);

        events.Select(e => (e.Kind, e.Quantity, e.ReasonCode)).Should().BeEquivalentTo(new[]
        {
            (JobOperationEventKind.Good, 4m, "TOOL"),
            (JobOperationEventKind.Scrap, 1m, "TOOL"),
            (JobOperationEventKind.Rework, 2m, "TOOL"),
        });
        (await verify.JobOperations.SingleAsync(r => r.JobId == jobId)).CompletedQuantity.Should().Be(4m);
    }

    [Fact]
    public async Task The_database_refuses_a_zero_quantity_event()
    {
        var (jobId, operationId) = await SeedAsync();
        await using var db = fixture.CreateContext();
        var service = Service(db);
        var (job, operation) = await service.FindRoutingStepAsync(jobId, operationId, CancellationToken.None);
        var row = await service.EnsureRowAsync(job, operation, CancellationToken.None);
        db.JobOperationEvents.Add(new JobOperationEvent
        {
            JobOperationId = row.Id,
            Kind = JobOperationEventKind.Good,
            Quantity = 0m,
            UserId = UserId,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<Npgsql.PostgresException>()
            .Which.ConstraintName.Should().Be("ck_job_operation_events_quantity");
    }
}
