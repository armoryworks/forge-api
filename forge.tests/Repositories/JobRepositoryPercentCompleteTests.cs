using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class JobRepositoryPercentCompleteTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T15:00:00Z");

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private sealed record Seed(TrackType Track, JobStage Stage, Part Routed, Operation Saw, Operation Mill, Part Unestimated, Part Unrouted);

    private static async Task<Seed> SeedAsync(AppDbContext db)
    {
        var track = new TrackType { Name = "Percent complete", Code = Unique("pc"), IsActive = true };
        var routed = new Part { PartNumber = Unique("P"), Description = "Routed" };
        var unestimated = new Part { PartNumber = Unique("P"), Description = "No standards" };
        var unrouted = new Part { PartNumber = Unique("P"), Description = "No routing" };
        db.AddRange(track, routed, unestimated, unrouted);
        await db.SaveChangesAsync();

        var stage = new JobStage { TrackTypeId = track.Id, Name = "Production", Code = "prod", SortOrder = 1, IsActive = true };
        var saw = new Operation { PartId = routed.Id, StepNumber = 10, Title = "Saw", SetupMinutes = 30m, RunMinutesEach = 2m, RunMinutesLot = 10m };
        var mill = new Operation { PartId = routed.Id, StepNumber = 20, Title = "Mill", SetupMinutes = 20m, EstimatedMs = 240000 };
        db.AddRange(stage, saw, mill);
        db.Operations.AddRange(
            new Operation { PartId = unestimated.Id, StepNumber = 10, Title = "Deburr" },
            new Operation { PartId = unestimated.Id, StepNumber = 20, Title = "Inspect" },
            new Operation { PartId = unestimated.Id, StepNumber = 30, Title = "Pack" });
        await db.SaveChangesAsync();
        return new Seed(track, stage, routed, saw, mill, unestimated, unrouted);
    }

    private static async Task<Job> AddJobAsync(AppDbContext db, Seed seed, string title, Part? part, decimal quantity = 10m)
    {
        var job = new Job
        {
            JobNumber = Unique("J"),
            Title = title,
            TrackTypeId = seed.Track.Id,
            CurrentStageId = seed.Stage.Id,
            PartId = part?.Id,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        if (part is not null)
        {
            db.JobParts.Add(new JobPart { JobId = job.Id, PartId = part.Id, Quantity = quantity });
            await db.SaveChangesAsync();
        }
        return job;
    }

    private static async Task AddRowAsync(
        AppDbContext db, Job job, Operation op, JobOperationStatus status, decimal good = 0m, decimal scrap = 0m)
    {
        db.JobOperations.Add(new JobOperation
        {
            JobId = job.Id,
            OperationId = op.Id,
            StepNumber = op.StepNumber,
            Title = op.Title,
            Status = status,
            CompletedQuantity = good,
            ScrapQuantity = scrap,
            EstSetupMinutes = op.SetupMinutes,
            EstRunMinutesEach = op.RunMinutesEach > 0m ? op.RunMinutesEach : (op.EstimatedMs ?? 0L) / 60000m,
            EstRunMinutesLot = op.RunMinutesLot,
        });
        await db.SaveChangesAsync();
    }

    private static JobListQuery Query(Seed seed, params string[] filters) =>
        new() { TrackTypeId = seed.Track.Id, PageSize = 200, Sort = "title", Order = "asc", Filter = [.. filters] };

    private static Task<PagedResponse<JobListResponseModel>> PageAsync(AppDbContext db, JobListQuery query) =>
        new JobRepository(db, new FixedClock(Now))
            .GetPagedJobsAsync(query, operationTracking: true, JobListFieldAccess.Full, CancellationToken.None);

    private static async Task<Dictionary<string, decimal?>> PercentByTitleAsync(AppDbContext db, Seed seed) =>
        (await PageAsync(db, Query(seed))).Items.ToDictionary(j => j.Title, j => j.PercentComplete);

    [Fact]
    public async Task Earns_setup_once_started_run_and_lot_minutes_for_good_pieces_and_everything_for_closed_steps()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        await AddJobAsync(db, seed, "untouched", seed.Routed);
        var setupOnly = await AddJobAsync(db, seed, "setup-only", seed.Routed);
        await AddRowAsync(db, setupOnly, seed.Saw, JobOperationStatus.InProgress);
        var partial = await AddJobAsync(db, seed, "partial", seed.Routed);
        await AddRowAsync(db, partial, seed.Saw, JobOperationStatus.InProgress, good: 4m, scrap: 1m);
        var scrapOnly = await AddJobAsync(db, seed, "scrap-only", seed.Routed);
        await AddRowAsync(db, scrapOnly, seed.Saw, JobOperationStatus.NotStarted, scrap: 2m);
        var oneClosed = await AddJobAsync(db, seed, "one-closed", seed.Routed);
        await AddRowAsync(db, oneClosed, seed.Saw, JobOperationStatus.Complete, good: 2m);
        await AddRowAsync(db, oneClosed, seed.Mill, JobOperationStatus.InProgress, good: 3m);
        var overrun = await AddJobAsync(db, seed, "overrun", seed.Routed);
        await AddRowAsync(db, overrun, seed.Mill, JobOperationStatus.InProgress, good: 30m);
        var allClosed = await AddJobAsync(db, seed, "all-closed", seed.Routed);
        await AddRowAsync(db, allClosed, seed.Saw, JobOperationStatus.Complete);
        await AddRowAsync(db, allClosed, seed.Mill, JobOperationStatus.Skipped);

        var percent = await PercentByTitleAsync(db, seed);

        percent["untouched"].Should().Be(0m);
        percent["setup-only"].Should().Be(25m);
        percent["partial"].Should().Be(35m);
        percent["scrap-only"].Should().Be(25m);
        percent["one-closed"].Should().Be(76m);
        percent["overrun"].Should().Be(50m);
        percent["all-closed"].Should().Be(100m);
    }

    [Fact]
    public async Task Uses_the_steps_own_estimates_and_the_sales_order_quantity_when_no_job_part_carries_one()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var customer = new Customer { Name = Unique("Cust") };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var order = new SalesOrder { CustomerId = customer.Id, OrderNumber = Unique("SO") };
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        var line = new SalesOrderLine { SalesOrderId = order.Id, Description = "Line", Quantity = 20m, UnitPrice = 1m, LineNumber = 1 };
        db.SalesOrderLines.Add(line);
        await db.SaveChangesAsync();

        var job = await AddJobAsync(db, seed, "from-line", null);
        job.PartId = seed.Routed.Id;
        job.SalesOrderLineId = line.Id;
        await db.SaveChangesAsync();
        db.JobOperations.Add(new JobOperation
        {
            JobId = job.Id, OperationId = seed.Saw.Id, StepNumber = 10, Title = "Saw",
            Status = JobOperationStatus.InProgress, CompletedQuantity = 10m,
            EstSetupMinutes = 0m, EstRunMinutesEach = 1m, EstRunMinutesLot = 0m,
        });
        await db.SaveChangesAsync();

        (await PercentByTitleAsync(db, seed))["from-line"].Should().Be(8m);
    }

    [Fact]
    public async Task Measures_against_the_target_quantity_when_the_job_starts_extra_pieces()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var reached = await AddJobAsync(db, seed, "target-reached", seed.Routed, quantity: 12m);
        reached.TargetQuantity = 10m;
        var halfway = await AddJobAsync(db, seed, "target-halfway", seed.Routed, quantity: 12m);
        halfway.TargetQuantity = 10m;
        await db.SaveChangesAsync();
        await AddRowAsync(db, reached, seed.Saw, JobOperationStatus.Complete, good: 11m);
        await AddRowAsync(db, reached, seed.Mill, JobOperationStatus.InProgress, good: 10m, scrap: 1m);
        await AddRowAsync(db, halfway, seed.Saw, JobOperationStatus.InProgress, good: 5m, scrap: 2m);

        var percent = await PercentByTitleAsync(db, seed);

        percent["target-reached"].Should().Be(100m);
        percent["target-halfway"].Should().Be(37m);
    }

    [Fact]
    public async Task Without_standard_minutes_it_is_the_share_of_closed_steps()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var job = await AddJobAsync(db, seed, "unestimated", seed.Unestimated);
        var steps = db.Operations.Where(o => o.PartId == seed.Unestimated.Id).OrderBy(o => o.StepNumber).ToList();
        await AddRowAsync(db, job, steps[0], JobOperationStatus.Complete);
        await AddRowAsync(db, job, steps[1], JobOperationStatus.Skipped);
        await AddRowAsync(db, job, steps[2], JobOperationStatus.InProgress, good: 5m);

        (await PercentByTitleAsync(db, seed))["unestimated"].Should().Be(66m);
    }

    [Fact]
    public async Task Jobs_without_a_routing_are_empty_unless_completed()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        await AddJobAsync(db, seed, "unrouted", seed.Unrouted);
        await AddJobAsync(db, seed, "partless", null);
        var done = await AddJobAsync(db, seed, "done", seed.Unrouted);
        done.CompletedDate = Now;
        var doneRouted = await AddJobAsync(db, seed, "done-routed", seed.Routed);
        doneRouted.CompletedDate = Now;
        await db.SaveChangesAsync();

        var percent = await PercentByTitleAsync(db, seed);

        percent["unrouted"].Should().BeNull();
        percent["partless"].Should().BeNull();
        percent["done"].Should().Be(100m);
        percent["done-routed"].Should().Be(100m);
    }

    [Theory]
    [InlineData("asc", new[] { "untouched", "partial", "closed", "unrouted" })]
    [InlineData("desc", new[] { "closed", "partial", "untouched", "unrouted" })]
    public async Task Sorts_with_empty_values_last_and_filters_by_range(string order, string[] expected)
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        await AddJobAsync(db, seed, "untouched", seed.Routed);
        var partial = await AddJobAsync(db, seed, "partial", seed.Routed);
        await AddRowAsync(db, partial, seed.Saw, JobOperationStatus.InProgress, good: 4m, scrap: 1m);
        var closed = await AddJobAsync(db, seed, "closed", seed.Routed);
        await AddRowAsync(db, closed, seed.Saw, JobOperationStatus.Complete);
        await AddRowAsync(db, closed, seed.Mill, JobOperationStatus.Complete);
        await AddJobAsync(db, seed, "unrouted", seed.Unrouted);

        (await PageAsync(db, Query(seed) with { Sort = "percentComplete", Order = order }))
            .Items.Select(j => j.Title).Should().Equal(expected);

        (await PageAsync(db, Query(seed, "percentComplete:range:30..100"))).Items.Select(j => j.Title)
            .Should().Equal("closed", "partial");
        (await PageAsync(db, Query(seed, "percentComplete:range:..41"))).Items.Select(j => j.Title)
            .Should().Equal("partial", "untouched");
    }
}
