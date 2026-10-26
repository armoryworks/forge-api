using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

[Collection(PostgresCollection.Name)]
public sealed class TimeEntryLaborCostPrecisionPostgresTests(PostgresFixture fixture)
{
    private const int UserId = 515151;
    private const decimal RatePerHour = 41.17m;

    private async Task<(int JobId, int EntryId)> SeedAsync()
    {
        await using var seed = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var track = new TrackType { Name = $"Cost {suffix}", Code = $"cost-{suffix}", IsActive = true };
        var workCenter = new WorkCenter { Name = $"Lathe {suffix}", Code = $"L-{suffix}", LaborCostPerHour = RatePerHour };
        var part = new Part { PartNumber = $"LC-{suffix}", Name = "Shaft" };
        seed.AddRange(track, workCenter, part);
        await seed.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Turning", Code = "turning", SortOrder = 1, IsActive = true };
        seed.JobStages.Add(stage);
        await seed.SaveChangesAsync();
        var job = new Job
        {
            JobNumber = $"J-LC-{suffix}",
            Title = "Kiosk-timed labour",
            TrackTypeId = track.Id,
            CurrentStageId = stage.Id,
            PartId = part.Id,
        };
        seed.Jobs.Add(job);
        await seed.SaveChangesAsync();
        var entry = new TimeEntry
        {
            UserId = UserId,
            JobId = job.Id,
            WorkCenterId = workCenter.Id,
            Date = new DateOnly(2026, 10, 23),
            DurationMinutes = 7,
        };
        seed.TimeEntries.Add(entry);
        await seed.SaveChangesAsync();
        return (job.Id, entry.Id);
    }

    [Fact]
    public async Task A_minutes_over_sixty_entry_stores_its_labour_cost_at_scale_four()
    {
        var (jobId, entryId) = await SeedAsync();
        await using var db = fixture.CreateContext();

        await new JobCostService(db).RecalculateTimeEntryCostsAsync(jobId, CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var scales = await verify.Database
            .SqlQuery<int>($"SELECT scale(actual_labor_cost) AS \"Value\" FROM time_entries WHERE id = {entryId}")
            .SingleAsync();
        scales.Should().BeLessThanOrEqualTo(4);
        var stored = await verify.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entryId);
        stored.ActualLaborCost.Should().Be(Math.Round(7m / 60m * RatePerHour, 4, MidpointRounding.AwayFromZero));
        stored.LaborCost.Should().Be(stored.ActualLaborCost);
    }

    [Fact]
    public async Task An_unbounded_labour_cost_written_directly_is_bounded_by_the_column_and_reads_back()
    {
        var (jobId, entryId) = await SeedAsync();
        await using var raw = fixture.CreateContext();
        await raw.Database.ExecuteSqlAsync(
            $"UPDATE time_entries SET actual_labor_cost = 7::numeric / 60 * 41.17 WHERE id = {entryId}");

        await using var verify = fixture.CreateContext();
        var entries = await verify.TimeEntries.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();

        entries.Should().ContainSingle().Which.ActualLaborCost.Should().Be(4.8032m);
    }

    [Fact]
    public async Task Disposing_a_job_with_kiosk_timed_labour_succeeds()
    {
        var (jobId, _) = await SeedAsync();
        await using var db = fixture.CreateContext();
        await new JobCostService(db).RecalculateTimeEntryCostsAsync(jobId, CancellationToken.None);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        var capabilities = new Mock<ICapabilitySnapshotProvider>();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDetailResponseModel)null!);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<BoardHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        var jobCost = new JobCostService(db);
        var handler = new DisposeJobHandler(
            new JobRepository(db, clock.Object), new AssetRepository(db), mediator.Object, hub.Object, clock.Object,
            capabilities.Object, db,
            new ProductionVariancePostingService(db, Mock.Of<IPostingEngine>(), capabilities.Object, jobCost));

        await handler.Handle(
            new DisposeJobCommand(jobId, new DisposeJobRequestModel(JobDisposition.AddToInventory, null), 1),
            CancellationToken.None);

        await using var verify = fixture.CreateContext();
        (await verify.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId)).Disposition
            .Should().Be(JobDisposition.AddToInventory);
        (await jobCost.GetActualLaborCostAtActualRateAsync(jobId, CancellationToken.None))
            .Should().Be(Math.Round(7m / 60m * RatePerHour, 4, MidpointRounding.AwayFromZero));
    }
}
