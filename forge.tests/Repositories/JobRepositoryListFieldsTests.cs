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
public sealed class JobRepositoryListFieldsTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T15:00:00Z");
    private static readonly JobListFieldAccess NoGatedFields = new(false, false, false, false);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private sealed record Seed(TrackType Track, JobStage Open, JobStage Done, Customer Customer, SalesOrder Order);

    private static async Task<Seed> SeedAsync(AppDbContext db)
    {
        var track = new TrackType { Name = "List fields", Code = Unique("lf"), IsActive = true };
        db.TrackTypes.Add(track);
        var customer = new Customer { Name = Unique("Cust") };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var open = new JobStage { TrackTypeId = track.Id, Name = "Open", Code = "o", SortOrder = 1, IsActive = true };
        var done = new JobStage { TrackTypeId = track.Id, Name = "Done", Code = "d", SortOrder = 2, IsActive = true };
        db.JobStages.AddRange(open, done);
        var order = new SalesOrder { CustomerId = customer.Id, OrderNumber = Unique("SO") };
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return new Seed(track, open, done, customer, order);
    }

    private static Job NewJob(Seed seed, string title, JobStage? stage = null) => new()
    {
        JobNumber = Unique("J"),
        Title = title,
        TrackTypeId = seed.Track.Id,
        CurrentStageId = (stage ?? seed.Open).Id,
    };

    private static JobListQuery Query(Seed seed, params string[] filters) =>
        new() { TrackTypeId = seed.Track.Id, PageSize = 200, Sort = "title", Order = "asc", Filter = [.. filters] };

    private static Task<PagedResponse<JobListResponseModel>> PageAsync(
        AppDbContext db, JobListQuery query, JobListFieldAccess? access = null) =>
        new JobRepository(db, new FixedClock(Now))
            .GetPagedJobsAsync(query, operationTracking: false, access ?? JobListFieldAccess.Full, CancellationToken.None);

    private static async Task<List<string>> TitlesAsync(AppDbContext db, JobListQuery query, JobListFieldAccess? access = null) =>
        (await PageAsync(db, query, access)).Items.Select(j => j.Title).ToList();

    [Fact]
    public async Task Text_filters_match_case_insensitively_with_each_operator()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        db.Jobs.AddRange(NewJob(seed, "Bracket Weld"), NewJob(seed, "bracket paint"), NewJob(seed, "Housing"));
        await db.SaveChangesAsync();

        (await TitlesAsync(db, Query(seed, "title:contains:BRACKET"))).Should().Equal("bracket paint", "Bracket Weld");
        (await TitlesAsync(db, Query(seed, "title:startsWith:hous"))).Should().Equal("Housing");
        (await TitlesAsync(db, Query(seed, "title:endsWith:WELD"))).Should().Equal("Bracket Weld");
        (await TitlesAsync(db, Query(seed, "title:equals:housing"))).Should().Equal("Housing");
        (await TitlesAsync(db, Query(seed, "title:notContains:bracket"))).Should().Equal("Housing");
        (await TitlesAsync(db, Query(seed, "title:notEquals:housing"))).Should().Equal("bracket paint", "Bracket Weld");
    }

    [Fact]
    public async Task Not_contains_keeps_jobs_whose_field_is_empty()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var withCustomer = NewJob(seed, "with");
        withCustomer.CustomerId = seed.Customer.Id;
        db.Jobs.AddRange(withCustomer, NewJob(seed, "without"));
        await db.SaveChangesAsync();

        (await TitlesAsync(db, Query(seed, $"customerName:notContains:{seed.Customer.Name}"))).Should().Equal("without");
    }

    [Fact]
    public async Task Enum_filters_take_ids_and_names()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var urgent = NewJob(seed, "urgent", seed.Done);
        urgent.Priority = JobPriority.Urgent;
        urgent.AssigneeId = 1;
        var scrapped = NewJob(seed, "scrapped");
        scrapped.Disposition = JobDisposition.Scrap;
        db.Jobs.AddRange(urgent, scrapped, NewJob(seed, "plain"));
        await db.SaveChangesAsync();

        (await TitlesAsync(db, Query(seed, $"currentStageId:in:{seed.Done.Id}"))).Should().Equal("urgent");
        (await TitlesAsync(db, Query(seed, "priorityName:in:urgent|High"))).Should().Equal("urgent");
        (await TitlesAsync(db, Query(seed, "disposition:in:Scrap"))).Should().Equal("scrapped");
        (await TitlesAsync(db, Query(seed, "assigneeId:in:1"))).Should().Equal("urgent");
        (await TitlesAsync(db, Query(seed, $"trackTypeId:in:{seed.Track.Id}"))).Should().HaveCount(3);
    }

    [Fact]
    public async Task Date_ranges_cover_whole_days_and_number_ranges_are_inclusive()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var first = NewJob(seed, "first");
        first.DueDate = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        first.TargetQuantity = 5m;
        var last = NewJob(seed, "last");
        last.DueDate = DateTimeOffset.Parse("2026-03-31T00:00:00Z");
        last.TargetQuantity = 10m;
        var after = NewJob(seed, "after");
        after.DueDate = DateTimeOffset.Parse("2026-04-01T00:00:00Z");
        after.TargetQuantity = 11m;
        db.Jobs.AddRange(first, last, after, NewJob(seed, "undated"));
        await db.SaveChangesAsync();

        (await TitlesAsync(db, Query(seed, "dueDate:range:2026-03-01..2026-03-31"))).Should().Equal("first", "last");
        (await TitlesAsync(db, Query(seed, "dueDate:range:2026-03-31.."))).Should().Equal("after", "last");
        (await TitlesAsync(db, Query(seed, "targetQuantity:range:5..10"))).Should().Equal("first", "last");
        (await TitlesAsync(db, Query(seed, "targetQuantity:range:..5"))).Should().Equal("first");
    }

    [Fact]
    public async Task Boolean_filters_read_overdue_and_holds_the_way_the_row_shows_them()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var late = NewJob(seed, "late");
        late.DueDate = Now.AddDays(-3);
        var held = NewJob(seed, "held");
        db.Jobs.AddRange(late, held, NewJob(seed, "fine"));
        await db.SaveChangesAsync();
        db.StatusEntries.Add(new StatusEntry
        {
            EntityType = "Job", EntityId = held.Id, StatusCode = "hold", StatusLabel = "Material", Category = "hold", StartedAt = Now,
        });
        await db.SaveChangesAsync();

        var overdue = await PageAsync(db, Query(seed, "isOverdue:in:true"));
        overdue.Items.Should().ContainSingle().Which.IsOverdue.Should().BeTrue();
        (await TitlesAsync(db, Query(seed, "isOverdue:in:false"))).Should().Equal("fine", "held");
        (await TitlesAsync(db, Query(seed, "onHold:in:true"))).Should().Equal("held");
        (await TitlesAsync(db, Query(seed, "onHold:in:true|false"))).Should().HaveCount(3);
    }

    [Fact]
    public async Task Filters_count_before_paging()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        for (var i = 0; i < 7; i++) db.Jobs.Add(NewJob(seed, $"match-{i}"));
        db.Jobs.Add(NewJob(seed, "other"));
        await db.SaveChangesAsync();

        var page = await PageAsync(db, Query(seed, "title:startsWith:match") with { PageSize = 3, Page = 3 });

        page.TotalCount.Should().Be(7);
        page.Items.Select(j => j.Title).Should().Equal("match-6");
    }

    [Theory]
    [InlineData("asc", new[] { "a", "b", "none" })]
    [InlineData("desc", new[] { "b", "a", "none" })]
    public async Task Field_sorts_keep_empty_values_last_in_both_directions(string order, string[] expected)
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var a = NewJob(seed, "a");
        a.CompletedDate = Now.AddDays(-2);
        var b = NewJob(seed, "b");
        b.CompletedDate = Now.AddDays(-1);
        db.Jobs.AddRange(a, b, NewJob(seed, "none"));
        await db.SaveChangesAsync();

        (await TitlesAsync(db, Query(seed) with { Sort = "completedDate", Order = order })).Should().Equal(expected);
    }

    [Fact]
    public async Task Sorts_by_stage_order_priority_and_quantity()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var low = NewJob(seed, "low", seed.Done);
        low.Priority = JobPriority.Low;
        low.TargetQuantity = 3m;
        var urgent = NewJob(seed, "urgent");
        urgent.Priority = JobPriority.Urgent;
        urgent.TargetQuantity = 1m;
        db.Jobs.AddRange(low, urgent);
        await db.SaveChangesAsync();

        (await TitlesAsync(db, Query(seed) with { Sort = "currentStageId" })).Should().Equal("urgent", "low");
        (await TitlesAsync(db, Query(seed) with { Sort = "priorityName", Order = "desc" })).Should().Equal("urgent", "low");
        (await TitlesAsync(db, Query(seed) with { Sort = "targetQuantity" })).Should().Equal("urgent", "low");
    }

    [Fact]
    public async Task Gated_fields_come_back_null_and_their_sorts_and_filters_do_nothing()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var line = new SalesOrderLine { SalesOrderId = seed.Order.Id, Description = "Line", Quantity = 2m, UnitPrice = 1m, LineNumber = 1 };
        db.SalesOrderLines.Add(line);
        await db.SaveChangesAsync();
        var zeta = NewJob(seed, "zeta");
        zeta.CustomerId = seed.Customer.Id;
        zeta.SalesOrderLineId = line.Id;
        zeta.ExternalRef = "INV-9";
        zeta.CompletedDate = Now.AddDays(-1);
        zeta.QuotedPrice = 100m;
        zeta.EstimatedLaborCost = 40m;
        var alpha = NewJob(seed, "alpha");
        alpha.QuotedPrice = 500m;
        db.Jobs.AddRange(zeta, alpha);
        await db.SaveChangesAsync();

        var full = (await PageAsync(db, Query(seed))).Items.Single(j => j.Title == "zeta");
        full.CustomerName.Should().Be(seed.Customer.Name);
        full.SalesOrderNumber.Should().Be(seed.Order.OrderNumber);
        full.BillingStatus.Should().Be("Uninvoiced");
        full.ExternalRef.Should().Be("INV-9");
        full.QuotedPrice.Should().Be(100m);
        full.EstimatedTotalCost.Should().Be(40m);

        var restricted = await PageAsync(db, Query(seed, "customerName:contains:x") with { Sort = "quotedPrice", Order = "desc" }, NoGatedFields);
        var row = restricted.Items.Single(j => j.Title == "zeta");
        row.CustomerName.Should().BeNull();
        row.CustomerId.Should().BeNull();
        row.SalesOrderId.Should().BeNull();
        row.SalesOrderNumber.Should().BeNull();
        row.BillingStatus.Should().BeNull();
        row.ExternalRef.Should().BeNull();
        row.QuotedPrice.Should().BeNull();
        row.EstimatedTotalCost.Should().BeNull();
        var unsorted = await PageAsync(db, Query(seed) with { Sort = null }, NoGatedFields);
        restricted.Items.Select(j => j.Title).Should().Equal(unsorted.Items.Select(j => j.Title));
        restricted.TotalCount.Should().Be(2);
        (await TitlesAsync(db, Query(seed) with { Sort = "quotedPrice", Order = "desc" })).Should().Equal("alpha", "zeta");
    }

    [Fact]
    public async Task Search_and_the_customer_param_skip_the_customer_for_callers_who_cannot_read_it()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var job = NewJob(seed, "owned");
        job.CustomerId = seed.Customer.Id;
        db.Jobs.AddRange(job, NewJob(seed, "unowned"));
        await db.SaveChangesAsync();

        var bySearch = Query(seed) with { Q = seed.Customer.Name };
        var byCustomer = Query(seed) with { CustomerId = seed.Customer.Id };

        (await TitlesAsync(db, bySearch)).Should().Equal("owned");
        (await TitlesAsync(db, bySearch, NoGatedFields)).Should().BeEmpty();
        (await TitlesAsync(db, byCustomer)).Should().Equal("owned");
        (await TitlesAsync(db, byCustomer, NoGatedFields)).Should().Equal("owned", "unowned");
    }

    [Fact]
    public async Task The_row_carries_its_stage_track_and_dates_for_the_table()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var job = NewJob(seed, "dated", seed.Done);
        job.StartDate = Now.AddDays(-5);
        job.CompletedDate = Now.AddDays(-1);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var row = (await PageAsync(db, Query(seed))).Items.Single();

        row.CurrentStageId.Should().Be(seed.Done.Id);
        row.TrackTypeId.Should().Be(seed.Track.Id);
        row.TrackTypeName.Should().Be(seed.Track.Name);
        row.StartDate.Should().Be(job.StartDate);
        row.CompletedDate.Should().Be(job.CompletedDate);
        row.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Archived_jobs_join_the_list_only_when_asked()
    {
        await using var db = fixture.CreateContext();
        var seed = await SeedAsync(db);
        var archived = NewJob(seed, "archived");
        archived.IsArchived = true;
        db.Jobs.AddRange(archived, NewJob(seed, "live"));
        await db.SaveChangesAsync();

        var everyTrack = new JobListQuery { PageSize = 200, Filter = [$"title:equals:archived"] };
        (await TitlesAsync(db, everyTrack)).Should().BeEmpty();
        (await TitlesAsync(db, everyTrack with { IncludeArchived = true })).Should().Equal("archived");
    }
}
