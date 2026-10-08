using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class JobRepositoryBoardFilterTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T15:00:00Z");

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static async Task<(TrackType Track, JobStage Stage)> SeedTrackAsync(AppDbContext db)
    {
        var track = new TrackType { Name = "Board filter", Code = Unique("bfilt"), IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();

        var stage = new JobStage { TrackTypeId = track.Id, Name = "Open", Code = "s1", SortOrder = 1, IsActive = true };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();
        return (track, stage);
    }

    private static Job NewJob(TrackType track, JobStage stage, string title) => new()
    {
        JobNumber = Unique("J"),
        Title = title,
        TrackTypeId = track.Id,
        CurrentStageId = stage.Id,
    };

    private static JobListQuery BoardQuery(int trackTypeId) =>
        new() { TrackTypeId = trackTypeId, Sort = "board", PageSize = 200 };

    private static Task<PagedResponse<JobListResponseModel>> PageAsync(AppDbContext db, JobListQuery query) =>
        new JobRepository(db, new FixedClock(Now)).GetPagedJobsAsync(query, operationTracking: false, CancellationToken.None);

    [Fact]
    public async Task Active_only_drops_completed_and_disposed_jobs()
    {
        await using var db = fixture.CreateContext();
        var (track, stage) = await SeedTrackAsync(db);

        var completed = NewJob(track, stage, "completed");
        completed.CompletedDate = Now.AddDays(-1);
        var disposed = NewJob(track, stage, "disposed");
        disposed.Disposition = JobDisposition.Scrap;
        db.Jobs.AddRange(NewJob(track, stage, "open"), completed, disposed);
        await db.SaveChangesAsync();

        var all = await PageAsync(db, BoardQuery(track.Id));
        var active = await PageAsync(db, BoardQuery(track.Id) with { ActiveOnly = true });

        all.TotalCount.Should().Be(3);
        active.TotalCount.Should().Be(1);
        active.Items.Select(j => j.Title).Should().Equal("open");
    }

    [Fact]
    public async Task Search_matches_job_number_title_part_number_and_customer_name()
    {
        await using var db = fixture.CreateContext();
        var (track, stage) = await SeedTrackAsync(db);

        var part = new Part { PartNumber = Unique("PN"), Description = "Searchable part" };
        var customer = new Customer { Name = Unique("Cust") };
        db.Parts.Add(part);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var byPart = NewJob(track, stage, "by-part");
        byPart.PartId = part.Id;
        var byCustomer = NewJob(track, stage, "by-customer");
        byCustomer.CustomerId = customer.Id;
        var byTitle = NewJob(track, stage, "Bracket weldment");
        var unrelated = NewJob(track, stage, "unrelated");
        db.Jobs.AddRange(byPart, byCustomer, byTitle, unrelated);
        await db.SaveChangesAsync();

        (await PageAsync(db, BoardQuery(track.Id) with { Q = part.PartNumber.ToLowerInvariant() }))
            .Items.Select(j => j.Title).Should().Equal("by-part");
        (await PageAsync(db, BoardQuery(track.Id) with { Q = customer.Name.ToUpperInvariant() }))
            .Items.Select(j => j.Title).Should().Equal("by-customer");
        (await PageAsync(db, BoardQuery(track.Id) with { Q = "  weldment " }))
            .Items.Select(j => j.Title).Should().Equal("Bracket weldment");
        (await PageAsync(db, BoardQuery(track.Id) with { Q = unrelated.JobNumber }))
            .Items.Select(j => j.Title).Should().Equal("unrelated");
    }

    [Fact]
    public async Task Customer_filter_keeps_only_that_customers_jobs()
    {
        await using var db = fixture.CreateContext();
        var (track, stage) = await SeedTrackAsync(db);

        var mine = new Customer { Name = Unique("Mine") };
        var other = new Customer { Name = Unique("Other") };
        db.Customers.AddRange(mine, other);
        await db.SaveChangesAsync();

        var a = NewJob(track, stage, "mine");
        a.CustomerId = mine.Id;
        var b = NewJob(track, stage, "other");
        b.CustomerId = other.Id;
        db.Jobs.AddRange(a, b, NewJob(track, stage, "none"));
        await db.SaveChangesAsync();

        var page = await PageAsync(db, BoardQuery(track.Id) with { CustomerId = mine.Id });

        page.TotalCount.Should().Be(1);
        page.Items.Select(j => j.Title).Should().Equal("mine");
    }

    [Fact]
    public async Task Overdue_only_keeps_open_jobs_due_before_today_and_agrees_with_the_card_flag()
    {
        await using var db = fixture.CreateContext();
        var (track, stage) = await SeedTrackAsync(db);

        var yesterday = NewJob(track, stage, "due-yesterday");
        yesterday.DueDate = DateTimeOffset.Parse("2026-10-07T23:59:00Z");
        var today = NewJob(track, stage, "due-today");
        today.DueDate = DateTimeOffset.Parse("2026-10-08T00:00:00Z");
        var tomorrow = NewJob(track, stage, "due-tomorrow");
        tomorrow.DueDate = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var lateButDone = NewJob(track, stage, "late-but-done");
        lateButDone.DueDate = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        lateButDone.CompletedDate = DateTimeOffset.Parse("2026-10-02T00:00:00Z");
        db.Jobs.AddRange(yesterday, today, tomorrow, lateButDone, NewJob(track, stage, "no-due-date"));
        await db.SaveChangesAsync();

        var overdue = await PageAsync(db, BoardQuery(track.Id) with { OverdueOnly = true });
        var all = await PageAsync(db, BoardQuery(track.Id));

        overdue.TotalCount.Should().Be(1);
        overdue.Items.Select(j => j.Title).Should().Equal("due-yesterday");
        all.Items.Where(j => j.IsOverdue).Select(j => j.Title).Should().Equal("due-yesterday");
    }

    [Fact]
    public async Task On_hold_only_keeps_jobs_with_an_open_hold()
    {
        await using var db = fixture.CreateContext();
        var (track, stage) = await SeedTrackAsync(db);

        var held = NewJob(track, stage, "held");
        var released = NewJob(track, stage, "released");
        var otherCategory = NewJob(track, stage, "workflow-status");
        db.Jobs.AddRange(held, released, otherCategory, NewJob(track, stage, "clear"));
        await db.SaveChangesAsync();

        db.StatusEntries.AddRange(
            new StatusEntry { EntityType = "Job", EntityId = held.Id, StatusCode = "material_hold", StatusLabel = "Material hold", Category = "hold", StartedAt = Now.AddDays(-2) },
            new StatusEntry { EntityType = "Job", EntityId = released.Id, StatusCode = "material_hold", StatusLabel = "Material hold", Category = "hold", StartedAt = Now.AddDays(-3), EndedAt = Now.AddDays(-1) },
            new StatusEntry { EntityType = "Job", EntityId = otherCategory.Id, StatusCode = "in_production", StatusLabel = "In production", Category = "workflow", StartedAt = Now.AddDays(-1) });
        await db.SaveChangesAsync();

        var page = await PageAsync(db, BoardQuery(track.Id) with { OnHoldOnly = true });

        page.TotalCount.Should().Be(1);
        page.Items.Single().Title.Should().Be("held");
        page.Items.Single().ActiveHolds.Should().Equal("Material hold");
    }

    [Fact]
    public async Task Filters_combine_and_total_count_reflects_them_before_paging()
    {
        await using var db = fixture.CreateContext();
        var (track, stage) = await SeedTrackAsync(db);

        for (var i = 0; i < 5; i++)
        {
            var late = NewJob(track, stage, $"late-{i}");
            late.DueDate = Now.AddDays(-3);
            db.Jobs.Add(late);
        }
        var lateDisposed = NewJob(track, stage, "late-disposed");
        lateDisposed.DueDate = Now.AddDays(-3);
        lateDisposed.Disposition = JobDisposition.AddToInventory;
        db.Jobs.AddRange(lateDisposed, NewJob(track, stage, "on-time"));
        await db.SaveChangesAsync();

        var page = await PageAsync(db, BoardQuery(track.Id) with { ActiveOnly = true, OverdueOnly = true, PageSize = 2 });

        page.TotalCount.Should().Be(5);
        page.Items.Should().HaveCount(2);
    }
}
