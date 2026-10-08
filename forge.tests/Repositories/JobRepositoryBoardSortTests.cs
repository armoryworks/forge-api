using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class JobRepositoryBoardSortTests(PostgresFixture fixture)
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static async Task<(TrackType Track, JobStage First, JobStage Second)> SeedTrackAsync(AppDbContext db)
    {
        var track = new TrackType { Name = "Board sort", Code = Unique("board"), IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();

        var second = new JobStage { TrackTypeId = track.Id, Name = "Second", Code = "s2", SortOrder = 2, IsActive = true };
        var first = new JobStage { TrackTypeId = track.Id, Name = "First", Code = "s1", SortOrder = 1, IsActive = true };
        db.JobStages.AddRange(second, first);
        await db.SaveChangesAsync();
        return (track, first, second);
    }

    private static Job NewJob(TrackType track, JobStage stage, int boardPosition, string title) => new()
    {
        JobNumber = Unique("J"),
        Title = title,
        TrackTypeId = track.Id,
        CurrentStageId = stage.Id,
        BoardPosition = boardPosition,
    };

    private static JobListQuery BoardQuery(int trackTypeId) =>
        new() { TrackTypeId = trackTypeId, Sort = "board", PageSize = 200 };

    [Fact]
    public async Task Board_sort_orders_by_stage_then_board_position_then_id()
    {
        await using var db = fixture.CreateContext();
        var (track, first, second) = await SeedTrackAsync(db);

        db.Jobs.AddRange(
            NewJob(track, second, 1, "second-1"),
            NewJob(track, first, 2, "first-2a"),
            NewJob(track, first, 2, "first-2b"),
            NewJob(track, first, 1, "first-1"));
        await db.SaveChangesAsync();

        var repo = new JobRepository(db, new FixedClock(DateTimeOffset.UtcNow));
        var page = await repo.GetPagedJobsAsync(BoardQuery(track.Id), false, CancellationToken.None);

        page.Items.Select(j => j.Title).Should().Equal("first-1", "first-2a", "first-2b", "second-1");
        page.Items.Select(j => j.BoardPosition).Should().Equal(1, 2, 2, 1);
    }

    [Fact]
    public async Task Board_list_carries_part_number_and_quantity()
    {
        await using var db = fixture.CreateContext();
        var (track, first, _) = await SeedTrackAsync(db);

        var part = new Part { PartNumber = Unique("P"), Description = "Board part" };
        var other = new Part { PartNumber = Unique("P"), Description = "Other part" };
        var customer = new Customer { Name = "Board customer" };
        db.Parts.AddRange(part, other);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var so = new SalesOrder { CustomerId = customer.Id, OrderNumber = Unique("SO") };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();
        var line = new SalesOrderLine { SalesOrderId = so.Id, PartId = part.Id, Description = "Line", Quantity = 40m, UnitPrice = 1m, LineNumber = 1 };
        db.SalesOrderLines.Add(line);
        await db.SaveChangesAsync();

        var fromLine = NewJob(track, first, 1, "from-line");
        fromLine.PartId = part.Id;
        fromLine.SalesOrderLineId = line.Id;
        fromLine.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 5m });

        var manual = NewJob(track, first, 2, "manual");
        manual.PartId = part.Id;
        manual.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 250m });
        manual.JobParts.Add(new JobPart { PartId = other.Id, Quantity = 7m });

        var lineWithoutJobPart = NewJob(track, first, 3, "line-without-job-part");
        lineWithoutJobPart.PartId = part.Id;
        lineWithoutJobPart.SalesOrderLineId = line.Id;

        var partless = NewJob(track, first, 4, "partless");

        db.Jobs.AddRange(fromLine, manual, lineWithoutJobPart, partless);
        await db.SaveChangesAsync();

        var repo = new JobRepository(db, new FixedClock(DateTimeOffset.UtcNow));
        var page = await repo.GetPagedJobsAsync(BoardQuery(track.Id), false, CancellationToken.None);

        var byTitle = page.Items.ToDictionary(j => j.Title);
        byTitle["from-line"].PartNumber.Should().Be(part.PartNumber);
        byTitle["from-line"].Quantity.Should().Be(5m);
        byTitle["line-without-job-part"].Quantity.Should().Be(40m);
        byTitle["manual"].PartNumber.Should().Be(part.PartNumber);
        byTitle["manual"].Quantity.Should().Be(250m);
        byTitle["partless"].PartNumber.Should().BeNull();
        byTitle["partless"].Quantity.Should().BeNull();
    }

    [Theory]
    [InlineData("2026-10-08T00:30:00Z", "2026-10-08T00:00:00Z", false)]
    [InlineData("2026-10-08T23:59:00Z", "2026-10-08T00:00:00Z", false)]
    [InlineData("2026-10-09T00:00:00Z", "2026-10-08T00:00:00Z", true)]
    [InlineData("2026-10-07T12:00:00Z", "2026-10-08T00:00:00Z", false)]
    public async Task Overdue_is_judged_by_calendar_date(string now, string due, bool expected)
    {
        await using var db = fixture.CreateContext();
        var (track, first, _) = await SeedTrackAsync(db);

        var job = NewJob(track, first, 1, "due");
        job.DueDate = DateTimeOffset.Parse(due);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var repo = new JobRepository(db, new FixedClock(DateTimeOffset.Parse(now)));
        var page = await repo.GetPagedJobsAsync(BoardQuery(track.Id), false, CancellationToken.None);
        var legacy = await repo.GetJobsAsync(track.Id, null, null, false, null, CancellationToken.None);

        page.Items.Single().IsOverdue.Should().Be(expected);
        legacy.Single().IsOverdue.Should().Be(expected);
    }

    [Fact]
    public async Task A_completed_job_is_never_overdue()
    {
        await using var db = fixture.CreateContext();
        var (track, first, _) = await SeedTrackAsync(db);

        var job = NewJob(track, first, 1, "done");
        job.DueDate = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        job.CompletedDate = DateTimeOffset.Parse("2026-10-02T00:00:00Z");
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var repo = new JobRepository(db, new FixedClock(DateTimeOffset.Parse("2026-10-09T00:00:00Z")));
        var page = await repo.GetPagedJobsAsync(BoardQuery(track.Id), false, CancellationToken.None);

        page.Items.Single().IsOverdue.Should().BeFalse();
    }
}
